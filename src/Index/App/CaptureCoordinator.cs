using Index.Actions;
using Index.Platform;
using Index.Capture;
using Index.UI.Editor;
using Index.Toolbar;
using Index.Settings;
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
    private readonly IVirtualWindowFrameSource _virtualWindowFrameSource;
    private readonly ICaptureImagePreparer _imagePreparer;
    private readonly ICapturePersistenceService _persistenceService;
    private readonly CaptureActionContextFactory _actionContextFactory;
    private readonly IShortcutSettingsStore _settingsStore;
    private DispatcherQueue? _uiDispatcher;
    private CaptureOverlaySessionController? _activeOverlaySession;
    private int _captureInProgress;
    private int _highResolutionTransitionInProgress;

    private static readonly string LogPath = @"C:\temp\index_capture.log";

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
    }

    public CaptureCoordinator(
        CaptureSource captureSource,
        IDisplayTopologyProvider displayTopologyProvider,
        CaptureActionExecutor actionExecutor,
        ISourceApplicationResolver sourceApplicationResolver,
        IWindowSurfaceCapture windowSurfaceCapture,
        IVirtualWindowFrameSource virtualWindowFrameSource,
        ICaptureImagePreparer imagePreparer,
        ICapturePersistenceService persistenceService,
        CaptureActionContextFactory actionContextFactory,
        IShortcutSettingsStore settingsStore)
    {
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _displayTopologyProvider = displayTopologyProvider
            ?? throw new ArgumentNullException(nameof(displayTopologyProvider));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _sourceApplicationResolver = sourceApplicationResolver
            ?? throw new ArgumentNullException(nameof(sourceApplicationResolver));
        _windowSurfaceCapture = windowSurfaceCapture
            ?? throw new ArgumentNullException(nameof(windowSurfaceCapture));
        _virtualWindowFrameSource = virtualWindowFrameSource
            ?? throw new ArgumentNullException(nameof(virtualWindowFrameSource));
        _imagePreparer = imagePreparer
            ?? throw new ArgumentNullException(nameof(imagePreparer));
        _persistenceService = persistenceService
            ?? throw new ArgumentNullException(nameof(persistenceService));
        _actionContextFactory = actionContextFactory
            ?? throw new ArgumentNullException(nameof(actionContextFactory));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    /// <summary>在 UI 线程调用，保存 dispatcher 供后续调度回 UI 线程创建 Window。</summary>
    public void InitOnUiThread()
    {
        _uiDispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public async Task BeginCaptureAsync(string source = "unknown")
    {
        if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
        {
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
            Log($"Capture failed: {ex}");
            EndCaptureSession();
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
                && Volatile.Read(ref _highResolutionTransitionInProgress) != 0)
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
                sourceSnapshot.Windows);
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
            Log($"Overlay failed: {ex}");
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
            Log($"pixel-edge preparation failed: {error.Message}");
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
            Interlocked.Exchange(ref _highResolutionTransitionInProgress, 1);
            return new CaptureOverlayDecisionHandling(
                DeferReleaseUntilCompleted: false,
                ContinueCaptureAfterRelease: true,
                () => ExecuteHighResolutionTransitionAsync(
                    result.Decision,
                    snapshots,
                    sourceSnapshot));
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

    private async Task ExecuteHighResolutionTransitionAsync(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        try
        {
            if (decision.TargetWindowHandle == nint.Zero)
                throw new InvalidOperationException("4K 截图需要框选一个可捕获的窗口。");

            var frozenDisplay = CaptureDecisionBinding.ResolveSnapshot(
                decision,
                frozenSnapshots);
            var requestedGlobalBounds = new SourceWindowBounds(
                frozenDisplay.Left + decision.Selection.X,
                frozenDisplay.Top + decision.Selection.Y,
                frozenDisplay.Left + decision.Selection.X + decision.Selection.Width,
                frozenDisplay.Top + decision.Selection.Y + decision.Selection.Height);
            var targetWindow = sourceSnapshot.Windows.FirstOrDefault(window =>
                window.IsTopLevel
                && window.Handle == decision.TargetWindowHandle);
            Log(targetWindow is null
                ? $"4K target: hwnd=0x{decision.TargetWindowHandle:X}, selection={requestedGlobalBounds}, " +
                  "frozen window metadata missing"
                : $"4K target: hwnd=0x{decision.TargetWindowHandle:X}, pid={targetWindow.ProcessId}, " +
                  $"title={targetWindow.WindowTitle}, bounds={targetWindow.Bounds}, " +
                  $"selection={requestedGlobalBounds}");
            if (targetWindow is null)
                throw new InvalidOperationException("The frozen 4K window identity is missing.");
            var frame = await Task.Run(() =>
                _virtualWindowFrameSource.CaptureAsync(
                    new VirtualWindowCaptureTarget(
                        targetWindow.Handle,
                        targetWindow.ProcessId,
                        targetWindow.Bounds)))
                .ConfigureAwait(false);
            var display = frame.Display;
            var mapping = HighResolutionCaptureGeometry.MapFromOriginalWindow(
                requestedGlobalBounds,
                frame.OriginalWindowBounds,
                display.Width,
                display.Height);
            var imageBounds = mapping.ImageBounds;
            var highResolutionSelection = new CaptureSelection
            {
                Display = CaptureDecisionBinding.IdentityOf(display),
                X = imageBounds.Left,
                Y = imageBounds.Top,
                Width = imageBounds.Width,
                Height = imageBounds.Height,
                Layers = HighResolutionCaptureGeometry.MapLayersFromOriginalSelection(
                    decision.Selection.Layers,
                    requestedGlobalBounds,
                    mapping)
            };
            var prepared = await Task.Run(() =>
                _imagePreparer.PrepareFrozen(highResolutionSelection, display.PngData))
                .ConfigureAwait(false);
            var capturedAt = DateTimeOffset.Now;
            var stored = await _persistenceService.SaveAsync(
                new CapturePersistenceRequest(
                    prepared,
                    highResolutionSelection,
                    display,
                    mapping.GlobalBounds,
                    sourceSnapshot,
                    capturedAt)).ConfigureAwait(false);
            string outputActionId = decision.ActionId == ToolbarCommandIds.HighResolution4K
                ? CaptureActionIds.Complete
                : decision.ActionId;
            var execution = await _actionExecutor.ExecuteAsync(
                outputActionId,
                _actionContextFactory.Create(
                    prepared,
                    new CaptureRegion(
                        mapping.GlobalBounds.Left,
                        mapping.GlobalBounds.Top,
                        highResolutionSelection.Width,
                        highResolutionSelection.Height),
                    capturedAt)).ConfigureAwait(false);
            if (!execution.IsSuccess)
                Log($"4K completion ended: {execution.Status}, {execution.Error}");
            Log($"4K shot stored directly: id={stored.Shot.Id}, sha={stored.Shot.Sha256}, " +
                $"size={highResolutionSelection.Width}x{highResolutionSelection.Height}");
            EndCaptureSession();
        }
        catch (Exception error)
        {
            Log($"4K transition failed: {error}");
            if (decision.ActionId != ToolbarCommandIds.HighResolution4K)
            {
                Log("automatic 4K failed; falling back to the original frozen selection");
                await ExecuteDecisionAsync(
                    decision,
                    frozenSnapshots,
                    sourceSnapshot).ConfigureAwait(false);
                EndCaptureSession();
                return;
            }
            ReopenFrozenOverlayAfterHighResolutionFailure(
                frozenSnapshots,
                sourceSnapshot);
        }
    }

    private void ReopenFrozenOverlayAfterHighResolutionFailure(
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        var dispatcher = _uiDispatcher;
        if (dispatcher is null
            || !dispatcher.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _highResolutionTransitionInProgress, 0);
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
                Log($"pin action ended: {execution.Status}, {execution.Error}");
                return;
            }

            Log("pin presented");
        }
        catch (Exception ex)
        {
            Log($"Pin preparation failed: {ex}");
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
            Log($"Pinned shot persistence failed: {ex}");
        }
    }

    private async Task ExecuteDecisionAsync(
        CaptureDecision decision,
        IReadOnlyList<DisplaySnapshot> frozenSnapshots,
        SourceApplicationSnapshot sourceSnapshot)
    {
        var result = decision.Selection;

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
            Log($"shot stored: id={stored.Shot.Id}, sha={stored.Shot.Sha256}, " +
                $"app={stored.Shot.AppName ?? "unknown"}, window={stored.Shot.WindowTitle ?? "unknown"}");

            var execution = await _actionExecutor.ExecuteAsync(
                decision.ActionId,
                _actionContextFactory.Create(prepared, pinnedRegion, capturedAt));
            if (!execution.IsSuccess)
                Log($"action {decision.ActionId} ended: {execution.Status}, {execution.Error}");
            else
                Log($"action {decision.ActionId} completed");
        }
        catch (Exception ex)
        {
            Log($"Action preparation failed: {ex}");
        }
    }

    private void EndCaptureSession()
    {
        Interlocked.Exchange(ref _highResolutionTransitionInProgress, 0);
        Interlocked.Exchange(ref _captureInProgress, 0);
    }
}
