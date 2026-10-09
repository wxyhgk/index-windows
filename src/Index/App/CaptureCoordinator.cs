using Index.Actions;
using Index.Platform;
using Index.Capture;
using Index.UI.Editor;
using Index.Toolbar;
using Index.Settings;
using Index.Platform.Diagnostics;
using Index.Platform.Clipboard;
using Index.Ocr;
using Microsoft.UI.Dispatching;
using System.Diagnostics;

namespace Index.App;

/// <summary>
/// 截图主流程编排：热键/按钮 → 冻结 → 框选 → 原图与元数据进入本地图库。
/// 对应 macOS 端 CaptureCoordinator。
/// </summary>
public sealed class CaptureCoordinator
{
    private readonly CaptureSource _captureSource;
    private readonly IDisplayTopologyProvider _displayTopologyProvider;
    private readonly CaptureActionExecutor _actionExecutor;
    private readonly ISourceApplicationResolver _sourceApplicationResolver;
    private readonly IWindowSurfaceCapture _windowSurfaceCapture;
    private readonly HighResolutionCaptureOrchestrator _highResolutionCapture;
    private readonly ICaptureImagePreparer _imagePreparer;
    private readonly ICapturePersistenceService _persistenceService;
    private readonly CaptureActionContextFactory _actionContextFactory;
    private readonly IShortcutSettingsStore _settingsStore;
    private readonly IOcrTextRecognizer _ocrTextRecognizer;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly IAppDiagnostics _diagnostics;
    private readonly CaptureSessionGate _captureSessionGate = new();
    private readonly CaptureTransitionTaskTracker _highResolutionTransitions = new();
    private DispatcherQueue? _uiDispatcher;
    private CaptureOverlaySessionController? _activeOverlaySession;
    private CancellationTokenSource? _highResolutionCancellation;
    private CaptureSelection? _lastSelection;

    public CaptureCoordinator(
        CaptureSource captureSource,
        IDisplayTopologyProvider displayTopologyProvider,
        CaptureActionExecutor actionExecutor,
        ISourceApplicationResolver sourceApplicationResolver,
        IWindowSurfaceCapture windowSurfaceCapture,
        HighResolutionCaptureOrchestrator highResolutionCapture,
        ICaptureImagePreparer imagePreparer,
        ICapturePersistenceService persistenceService,
        CaptureActionContextFactory actionContextFactory,
        IShortcutSettingsStore settingsStore,
        IOcrTextRecognizer ocrTextRecognizer,
        IClipboardWriter clipboardWriter,
        IAppDiagnostics diagnostics)
    {
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _displayTopologyProvider = displayTopologyProvider
            ?? throw new ArgumentNullException(nameof(displayTopologyProvider));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _sourceApplicationResolver = sourceApplicationResolver
            ?? throw new ArgumentNullException(nameof(sourceApplicationResolver));
        _windowSurfaceCapture = windowSurfaceCapture
            ?? throw new ArgumentNullException(nameof(windowSurfaceCapture));
        _highResolutionCapture = highResolutionCapture
            ?? throw new ArgumentNullException(nameof(highResolutionCapture));
        _imagePreparer = imagePreparer
            ?? throw new ArgumentNullException(nameof(imagePreparer));
        _persistenceService = persistenceService
            ?? throw new ArgumentNullException(nameof(persistenceService));
        _actionContextFactory = actionContextFactory
            ?? throw new ArgumentNullException(nameof(actionContextFactory));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _ocrTextRecognizer = ocrTextRecognizer
            ?? throw new ArgumentNullException(nameof(ocrTextRecognizer));
        _clipboardWriter = clipboardWriter
            ?? throw new ArgumentNullException(nameof(clipboardWriter));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private void Log(string message)
        => QueueDiagnostic(
            AppDiagnosticLevel.Trace,
            "capture-trace",
            new Dictionary<string, string?> { ["message"] = message });

    private void LogFailure(
        string eventName,
        Exception error,
        IReadOnlyDictionary<string, string?>? properties = null)
        => QueueDiagnostic(
            AppDiagnosticLevel.Error,
            eventName,
            properties,
            error);

    private void QueueDiagnostic(
        AppDiagnosticLevel level,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties,
        Exception? exception = null)
    {
        var diagnostics = _diagnostics;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                diagnostics.Write(
                    level,
                    "capture",
                    eventName,
                    properties,
                    exception);
            }
            catch
            {
            }
        });
    }

    /// <summary>在 UI 线程调用，保存 dispatcher 供后续调度回 UI 线程创建 Window。</summary>
    public void InitOnUiThread()
    {
        _uiDispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public CaptureShutdownWork BeginShutdown()
    {
        _captureSessionGate.Shutdown();
        var restoration = _highResolutionTransitions.CloseAndGetCurrent();
        _activeOverlaySession?.Cancel();
        ReleaseHighResolutionCancellation(cancel: true);

        return new CaptureShutdownWork(
            "overlay-high-resolution",
            restoration);
    }

    public async Task BeginCaptureAsync(string source = "unknown")
    {
        if (!_captureSessionGate.TryBeginCapture())
        {
            if (_captureSessionGate.IsShutdown)
                return;

            Log($"BeginCapture ({source}) requested while another capture is active");
            RecoverOrReactivateOverlay(source);
            return;
        }

        long captureStarted = Stopwatch.GetTimestamp();
        Log($"BeginCapture ({source}) start, thread={Environment.CurrentManagedThreadId}");
        try
        {
            // Freeze HWND, Z order and bounds before the first asynchronous monitor capture.
            var sourceSnapshot = _sourceApplicationResolver.CaptureSnapshot();
            Log($"source snapshot captured, elapsed={ElapsedMilliseconds(captureStarted):F0}ms");

            var snapshots = await _captureSource.MakeSnapshotsAsync();
            Log($"frozen {snapshots.Count} display(s), " +
                $"elapsed={ElapsedMilliseconds(captureStarted):F0}ms, " +
                $"thread={Environment.CurrentManagedThreadId}");

            // 覆盖层一旦激活，前台窗口就会变成 Index。和 macOS 一样，
            // 必须在显示覆盖层之前冻结窗口 Z 序与前台应用。
            ShowOverlay(snapshots, sourceSnapshot, captureStarted);
        }
        catch (Exception ex)
        {
            LogFailure("capture-failed", ex);
            EndCaptureSession();
        }
    }

    /// <summary>延迟截图：显示 3 秒倒计时，结束后冻结画面。</summary>
    public void BeginDelayedCapture()
    {
        if (_uiDispatcher is not { } dispatcher)
        {
            Log("BeginDelayedCapture: no UI dispatcher");
            return;
        }

        if (!dispatcher.TryEnqueue(() =>
        {
            var countdown = new OverlayCountdownWindow();
            countdown.Completed += () =>
            {
                if (dispatcher.TryEnqueue(() => _ = BeginCaptureAsync("delayed")))
                    Log("delayed capture: countdown completed, starting capture");
            };
            countdown.ShowAndStart();
        }))
        {
            Log("BeginDelayedCapture: dispatch failed");
        }
    }

    private void RecoverOrReactivateOverlay(string source)
    {
        void Recover()
        {
            var session = _activeOverlaySession;
            if (session is not null && session.TryReactivate())
            {
                Log("active overlay reactivated");
                return;
            }

            if (session is null
                && _captureSessionGate.IsHighResolutionTransition)
            {
                Log("capture hotkey ignored while the 4K frame is being prepared");
                return;
            }

            Log("active capture gate had no visible overlay; releasing stale session");
            session?.Cancel();
            if (session is null)
                EndCaptureSession();
            _ = BeginCaptureAsync($"{source}-recovered");
        }

        if (_uiDispatcher is { } dispatcher && !dispatcher.HasThreadAccess)
        {
            if (!dispatcher.TryEnqueue(Recover))
                Log("failed to dispatch stale overlay recovery");
            return;
        }
        Recover();
    }

    private void ShowOverlay(
        IReadOnlyList<DisplaySnapshot> snapshots,
        SourceApplicationSnapshot sourceSnapshot,
        long captureStarted)
    {
        // Window 是线程亲和的，必须调度回 UI 线程创建
        if (_uiDispatcher != null)
        {
            if (!_uiDispatcher.TryEnqueue(
                    DispatcherQueuePriority.Normal,
                    () => CreateOverlay(snapshots, sourceSnapshot, captureStarted)))
            {
                Log("Overlay dispatch failed");
                EndCaptureSession();
            }
        }
        else
            CreateOverlay(snapshots, sourceSnapshot, captureStarted);
    }

    private void CreateOverlay(
        IReadOnlyList<DisplaySnapshot> snapshots,
        SourceApplicationSnapshot sourceSnapshot,
        long captureStarted)
    {
        Log($"creating overlay, elapsed={ElapsedMilliseconds(captureStarted):F0}ms, " +
            $"thread={Environment.CurrentManagedThreadId}");
        try
        {
            var frozenTopology = DisplayTopologySnapshot.FromSnapshots(snapshots);
            var currentTopology = _displayTopologyProvider.GetCurrentTopology();
            if (!DisplayTopology.Matches(frozenTopology, currentTopology))
            {
                Log("Overlay rejected: display topology changed after freezing");
                EndCaptureSession();
                return;
            }

            var session = new SelectionOverlaySession(
                snapshots,
                sourceSnapshot.Windows,
                _ocrTextRecognizer,
                _clipboardWriter,
                _diagnostics,
                _lastSelection);
            var overlayDispatcher = DispatcherQueue.GetForCurrentThread()
                ?? _uiDispatcher
                ?? throw new InvalidOperationException("Overlay creation requires a UI dispatcher.");
            var controller = new CaptureOverlaySessionController(
                session,
                overlayDispatcher,
                result => RouteOverlayDecision(result, snapshots, sourceSnapshot),
                OnOverlayReleased,
                Log);
            controller.PixelEdgeDetectionRequested += snapshot =>
                _ = PreparePixelEdgeDetectorAsync(
                    controller,
                    snapshot,
                    overlayDispatcher,
                    captureStarted);
            _activeOverlaySession = controller;
            controller.Show();
            Log($"overlay session shown: {snapshots.Count} display(s), " +
                $"elapsed={ElapsedMilliseconds(captureStarted):F0}ms");
        }
        catch (Exception ex)
        {
            LogFailure("overlay-failed", ex);
            var activeSession = _activeOverlaySession;
            if (activeSession is null)
                EndCaptureSession();
            else
                activeSession.Dispose();
        }
    }

    private async Task PreparePixelEdgeDetectorAsync(
        CaptureOverlaySessionController controller,
        DisplaySnapshot snapshot,
        DispatcherQueue dispatcher,
        long captureStarted)
    {
        var cancellationToken = controller.LifetimeToken;
        try
        {
            var detector = await Task.Run(() =>
                FrozenPixelEdgeDetectorFactory.TryCreate(snapshot, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!dispatcher.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(_activeOverlaySession, controller)
                        || !controller.TrySetPixelEdgeDetector(snapshot.DisplayId, detector))
                    {
                        return;
                    }

                    Log($"pixel-edge index ready: display={snapshot.DisplayId}, " +
                        $"elapsed={ElapsedMilliseconds(captureStarted):F0}ms");
                }))
            {
                Log("pixel-edge indexes completed after the overlay dispatcher stopped");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"pixel-edge indexing canceled: display={snapshot.DisplayId}");
        }
        catch (Exception error)
        {
            // Pixel snapping is optional. Window snapping and manual selection remain usable.
            LogFailure("pixel-edge-preparation-failed", error);
        }
    }

    private static double ElapsedMilliseconds(long startedTimestamp)
        => Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    private CaptureOverlayDecisionHandling RouteOverlayDecision(
        SelectionOverlayDecision result,
        IReadOnlyList<DisplaySnapshot> snapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        if (ShouldUseHighResolution(result.Decision))
        {
            if (!_captureSessionGate.TryBeginHighResolutionTransition())
            {
                Log("4K transition rejected because the capture session is no longer active");
                return new CaptureOverlayDecisionHandling(
                    DeferReleaseUntilCompleted: false,
                    ContinueCaptureAfterRelease: false,
                    () => Task.CompletedTask);
            }

            var cancellation = new CancellationTokenSource();
            var previousCancellation = Interlocked.Exchange(
                ref _highResolutionCancellation,
                cancellation);
            previousCancellation?.Cancel();
            previousCancellation?.Dispose();
            var cancellationToken = cancellation.Token;
            return new CaptureOverlayDecisionHandling(
                DeferReleaseUntilCompleted: false,
                ContinueCaptureAfterRelease: true,
                () => TrackHighResolutionTransition(
                    result.Decision,
                    snapshots,
                    sourceSnapshot,
                    cancellationToken));
        }

        if (result.Decision.ActionId == CaptureActionIds.Pin)
        {
            return new CaptureOverlayDecisionHandling(
                DeferReleaseUntilCompleted: true,
                ContinueCaptureAfterRelease: false,
                () => ExecutePinnedDecisionAsync(
                    result.Decision,
                    snapshots,
                    sourceSnapshot));
        }

        return new CaptureOverlayDecisionHandling(
            DeferReleaseUntilCompleted: false,
            ContinueCaptureAfterRelease: false,
            () => ExecuteDecisionAsync(result.Decision, snapshots, sourceSnapshot));
    }

    private Task TrackHighResolutionTransition(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot,
        CancellationToken cancellationToken)
    {
        if (_highResolutionTransitions.TryTrack(
                () => ObserveHighResolutionTransitionAsync(
                    decision,
                    frozenSnapshots,
                    sourceSnapshot,
                    cancellationToken),
                out var transition))
        {
            return transition;
        }

        // Shutdown may close the tracker after the overlay decision was routed but before its
        // deferred callback runs. Do not start a new display/window mutation after that point.
        ReleaseHighResolutionCancellation(cancel: true);
        return Task.CompletedTask;
    }

    private bool ShouldUseHighResolution(CaptureDecision decision)
    {
        if (decision.TargetWindowHandle == nint.Zero)
            return false;
        if (decision.ActionId == ToolbarCommandIds.HighResolution4K)
            return true;
        return Automatic4KCapturePolicy.ShouldUpgrade(
            _settingsStore.Current.Automatic4KCapture,
            decision.ActionId,
            decision.TargetWindowHandle);
    }

    private void OnOverlayReleased(
        CaptureOverlaySessionController controller,
        bool continueCapture)
    {
        if (!ReferenceEquals(_activeOverlaySession, controller))
            return;

        _activeOverlaySession = null;
        if (!continueCapture)
            EndCaptureSession();
    }

    private async Task ObserveHighResolutionTransitionAsync(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteHighResolutionTransitionAsync(
                decision,
                frozenSnapshots,
                sourceSnapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log("4K transition canceled before a structured outcome was returned");
            EndCaptureSession();
        }
        catch (Exception error)
        {
            // Non-deferred overlay work is launched after the overlay is released. Observe every
            // unexpected failure here so it cannot fault silently and leave the session gate held.
            LogFailure("high-resolution-transition-failed", error);
            EndCaptureSession();
        }
    }

    private async Task ExecuteHighResolutionTransitionAsync(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot,
        CancellationToken cancellationToken)
    {
        var mode = decision.ActionId == ToolbarCommandIds.HighResolution4K
            ? HighResolutionCaptureMode.Explicit
            : HighResolutionCaptureMode.Automatic;
        var outcome = await _highResolutionCapture.ExecuteAsync(
            new HighResolutionCaptureRequest(
                decision,
                frozenSnapshots,
                sourceSnapshot,
                mode),
            cancellationToken).ConfigureAwait(false);

        if (outcome.IsStored)
        {
            if (outcome.ActionExecution is { IsSuccess: false } action)
            {
                if (action.Error is { } actionError)
                {
                    LogFailure(
                        "high-resolution-action-failed",
                        actionError,
                        new Dictionary<string, string?>
                        {
                            ["status"] = action.Status.ToString()
                        });
                }
                else
                {
                    Log($"4K completion ended: {action.Status}");
                }
            }
            if (outcome.StoredCapture is { } stored)
            {
                Log($"4K shot stored directly: id={stored.Shot.Id}, sha={stored.Shot.Sha256}, " +
                    $"size={outcome.Width}x{outcome.Height}");
            }
        }
        else if (outcome.Error is { } error)
        {
            LogFailure(
                "high-resolution-transition-ended-with-error",
                error,
                new Dictionary<string, string?>
                {
                    ["status"] = outcome.Status.ToString()
                });
        }

        switch (outcome.Continuation)
        {
            case HighResolutionCaptureContinuation.ExecuteFrozenDecision:
                Log("automatic 4K failed; falling back to the original frozen selection");
                await ExecuteDecisionAsync(
                    decision,
                    frozenSnapshots,
                    sourceSnapshot).ConfigureAwait(false);
                EndCaptureSession();
                break;
            case HighResolutionCaptureContinuation.ResumeFrozenSelection:
                ReopenFrozenOverlayAfterHighResolutionFailure(
                    frozenSnapshots,
                    sourceSnapshot);
                break;
            default:
                EndCaptureSession();
                break;
        }
    }

    private void ReopenFrozenOverlayAfterHighResolutionFailure(
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        if (_captureSessionGate.IsShutdown)
        {
            EndCaptureSession();
            return;
        }
        var dispatcher = _uiDispatcher;
        if (dispatcher is null
            || !dispatcher.TryEnqueue(() =>
            {
                if (_captureSessionGate.IsShutdown)
                {
                    EndCaptureSession();
                    return;
                }
                ReleaseHighResolutionCancellation(cancel: false);
                if (!_captureSessionGate.TryResumeCaptureAfterHighResolutionFailure())
                {
                    EndCaptureSession();
                    return;
                }
                CreateOverlay(
                    frozenSnapshots,
                    sourceSnapshot,
                    Stopwatch.GetTimestamp());
            }))
        {
            EndCaptureSession();
        }
    }

    private async Task ExecutePinnedDecisionAsync(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        var result = decision.Selection;
        try
        {
            var snapshot = CaptureDecisionBinding.ResolveSnapshot(decision, frozenSnapshots);
            var globalRegion = new SourceWindowBounds(
                snapshot.Left + result.X,
                snapshot.Top + result.Y,
                snapshot.Left + result.X + result.Width,
                snapshot.Top + result.Y + result.Height);
            var screenRegion = new CaptureRegion(
                globalRegion.Left,
                globalRegion.Top,
                result.Width,
                result.Height);
            var capturedAt = DateTimeOffset.Now;

            // A pin is the frozen selection changing into a movable state. Build it directly
            // from that already-frozen frame; browser metadata, compositor recapture and the
            // database must not sit on the interaction's critical path.
            var prepared = await Task.Run(() =>
                _imagePreparer.PrepareFrozen(result, snapshot.PngData)).ConfigureAwait(false);
            var execution = await _actionExecutor.ExecuteAsync(
                decision.ActionId,
                _actionContextFactory.Create(prepared, screenRegion, capturedAt));
            _ = PersistPinnedCaptureAsync(
                new CapturePersistenceRequest(
                    prepared,
                    result,
                    snapshot,
                    globalRegion,
                    sourceSnapshot,
                    capturedAt));
            if (!execution.IsSuccess)
            {
                if (execution.Error is { } executionError)
                {
                    LogFailure(
                        "pin-action-failed",
                        executionError,
                        new Dictionary<string, string?>
                        {
                            ["status"] = execution.Status.ToString()
                        });
                }
                else
                {
                    Log($"pin action ended: {execution.Status}");
                }
                return;
            }

            Log("pin presented");
        }
        catch (Exception ex)
        {
            LogFailure("pin-preparation-failed", ex);
        }
    }

    private async Task PersistPinnedCaptureAsync(
        CapturePersistenceRequest request)
    {
        try
        {
            var stored = await _persistenceService
                .SaveAsync(request)
                .ConfigureAwait(false);
            Log($"pinned shot stored: id={stored.Shot.Id}, sha={stored.Shot.Sha256}");
        }
        catch (Exception ex)
        {
            LogFailure("pinned-shot-persistence-failed", ex);
        }
    }

    private async Task ExecuteDecisionAsync(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        var result = decision.Selection;
        _lastSelection = result;

        try
        {
            // The overlay result owns its display identity. Never infer it from list order:
            // monitor enumeration can change and each overlay uses display-local coordinates.
            var snapshot = CaptureDecisionBinding.ResolveSnapshot(decision, frozenSnapshots);
            Log($"selection: display={snapshot.DisplayId} X={result.X} Y={result.Y} " +
                $"W={result.Width} H={result.Height}");
            var globalRegion = new SourceWindowBounds(
                snapshot.Left + result.X,
                snapshot.Top + result.Y,
                snapshot.Left + result.X + result.Width,
                snapshot.Top + result.Y + result.Height);
            var pinnedRegion = new CaptureRegion(
                globalRegion.Left,
                globalRegion.Top,
                result.Width,
                result.Height);
            var sourceApplication = _sourceApplicationResolver.Resolve(
                sourceSnapshot,
                globalRegion);

            PreparedCaptureImage? prepared = null;
            var directTarget = BrowserWindowCapturePolicy.FindTarget(
                sourceSnapshot,
                globalRegion,
                sourceApplication);
            if (directTarget is not null)
            {
                var directPng = await _windowSurfaceCapture.TryCapturePngAsync(
                    directTarget.Handle,
                    TimeSpan.FromSeconds(2));
                prepared = directPng is null
                    ? null
                    : _imagePreparer.TryPrepareDirect(result, directPng);
                Log(prepared is null
                    ? "browser compositor capture unavailable or size mismatch; using frozen frame"
                    : $"browser compositor capture used: hwnd=0x{directTarget.Handle:X}");
            }

            prepared ??= await Task.Run(() =>
                _imagePreparer.PrepareFrozen(result, snapshot.PngData));
            var capturedAt = DateTimeOffset.Now;

            // 和 macOS 一致：无论选择哪个输出动作，都先保存未标注原图；
            // 标注只进入 append-only revision，不烧进 original。
            var stored = await _persistenceService.SaveAsync(
                new CapturePersistenceRequest(
                    prepared,
                    result,
                    snapshot,
                    globalRegion,
                    sourceSnapshot,
                    capturedAt));
            Log($"shot stored: id={stored.Shot.Id}, sha={stored.Shot.Sha256}");

            var execution = await _actionExecutor.ExecuteAsync(
                decision.ActionId,
                _actionContextFactory.Create(prepared, pinnedRegion, capturedAt));
            if (!execution.IsSuccess)
            {
                if (execution.Error is { } executionError)
                {
                    LogFailure(
                        "capture-action-failed",
                        executionError,
                        new Dictionary<string, string?>
                        {
                            ["actionId"] = decision.ActionId,
                            ["status"] = execution.Status.ToString()
                        });
                }
                else
                {
                    Log($"action {decision.ActionId} ended: {execution.Status}");
                }
            }
            else
                Log($"action {decision.ActionId} completed");
        }
        catch (Exception ex)
        {
            LogFailure("action-preparation-failed", ex);
        }
    }

    private void EndCaptureSession()
    {
        ReleaseHighResolutionCancellation(cancel: true);
        _captureSessionGate.EndCapture();
    }

    private void ReleaseHighResolutionCancellation(bool cancel)
    {
        var cancellation = Interlocked.Exchange(ref _highResolutionCancellation, null);
        if (cancellation is null)
            return;
        try
        {
            if (cancel)
                cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
