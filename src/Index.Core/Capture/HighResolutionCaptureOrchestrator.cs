using Index.Actions;
using Index.Platform;
using Index.Storage;

namespace Index.Capture;

public enum HighResolutionCaptureMode
{
    Explicit,
    Automatic
}

public enum HighResolutionCaptureStatus
{
    Stored,
    Canceled,
    Failed
}

/// <summary>
/// Tells the capture-session owner what to do after the dense-window attempt. The orchestrator
/// deliberately does not know about overlays, dispatchers or capture-session gates.
/// </summary>
public enum HighResolutionCaptureContinuation
{
    CompleteSession,
    ExecuteFrozenDecision,
    ResumeFrozenSelection
}

public sealed record HighResolutionCaptureRequest(
    CaptureDecision Decision,
    IReadOnlyList<DisplaySnapshot> FrozenSnapshots,
    SourceApplicationSnapshot SourceSnapshot,
    HighResolutionCaptureMode Mode);

public sealed record HighResolutionCaptureOutcome(
    HighResolutionCaptureStatus Status,
    HighResolutionCaptureContinuation Continuation,
    StoredCapture? StoredCapture = null,
    CaptureActionExecutionResult? ActionExecution = null,
    int Width = 0,
    int Height = 0,
    Exception? Error = null)
{
    public bool IsStored => Status == HighResolutionCaptureStatus.Stored;
}

/// <summary>
/// Captures an immutable window identity at higher density, maps the frozen selection into that
/// frame, persists it and runs the originally requested output action. UI recovery remains the
/// responsibility of the capture-session owner through <see cref="HighResolutionCaptureOutcome"/>.
/// </summary>
public sealed class HighResolutionCaptureOrchestrator
{
    private readonly IVirtualWindowFrameSource _frameSource;
    private readonly ICaptureImagePreparer _imagePreparer;
    private readonly ICapturePersistenceService _persistenceService;
    private readonly CaptureActionExecutor _actionExecutor;
    private readonly CaptureActionContextFactory _actionContextFactory;
    private readonly TimeProvider _clock;

    public HighResolutionCaptureOrchestrator(
        IVirtualWindowFrameSource frameSource,
        ICaptureImagePreparer imagePreparer,
        ICapturePersistenceService persistenceService,
        CaptureActionExecutor actionExecutor,
        CaptureActionContextFactory actionContextFactory,
        TimeProvider? clock = null)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _imagePreparer = imagePreparer ?? throw new ArgumentNullException(nameof(imagePreparer));
        _persistenceService = persistenceService
            ?? throw new ArgumentNullException(nameof(persistenceService));
        _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
        _actionContextFactory = actionContextFactory
            ?? throw new ArgumentNullException(nameof(actionContextFactory));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<HighResolutionCaptureOutcome> ExecuteAsync(
        HighResolutionCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Decision);
        ArgumentNullException.ThrowIfNull(request.FrozenSnapshots);
        ArgumentNullException.ThrowIfNull(request.SourceSnapshot);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = request.Decision;
            if (decision.TargetWindowHandle == nint.Zero)
                throw new InvalidOperationException("A high-resolution capture requires a window target.");

            var frozenDisplay = CaptureDecisionBinding.ResolveSnapshot(
                decision,
                request.FrozenSnapshots);
            var requestedGlobalBounds = new SourceWindowBounds(
                checked(frozenDisplay.Left + decision.Selection.X),
                checked(frozenDisplay.Top + decision.Selection.Y),
                checked(frozenDisplay.Left + decision.Selection.X + decision.Selection.Width),
                checked(frozenDisplay.Top + decision.Selection.Y + decision.Selection.Height));
            var targetWindow = request.SourceSnapshot.Windows.FirstOrDefault(window =>
                window.IsTopLevel
                && window.Handle == decision.TargetWindowHandle);
            if (targetWindow is null)
                throw new InvalidOperationException("The frozen high-resolution window identity is missing.");

            var frame = await Task.Run(
                () => _frameSource.CaptureAsync(
                    new VirtualWindowCaptureTarget(
                        targetWindow.Handle,
                        targetWindow.ProcessId,
                        targetWindow.Bounds),
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var display = frame.Display;
            var mapping = HighResolutionCaptureGeometry.MapFromOriginalWindow(
                requestedGlobalBounds,
                frame.OriginalWindowBounds,
                display.Width,
                display.Height);
            var imageBounds = mapping.ImageBounds;
            var selection = new CaptureSelection
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
            var prepared = await Task.Run(
                () => _imagePreparer.PrepareFrozen(selection, display.PngData),
                cancellationToken).ConfigureAwait(false);
            var capturedAt = _clock.GetLocalNow();
            var stored = await _persistenceService.SaveAsync(
                new CapturePersistenceRequest(
                    prepared,
                    selection,
                    display,
                    mapping.GlobalBounds,
                    request.SourceSnapshot,
                    capturedAt),
                cancellationToken).ConfigureAwait(false);

            string outputActionId = request.Mode == HighResolutionCaptureMode.Explicit
                ? CaptureActionIds.Complete
                : decision.ActionId;
            var actionExecution = await _actionExecutor.ExecuteAsync(
                outputActionId,
                _actionContextFactory.Create(
                    prepared,
                    new CaptureRegion(
                        mapping.GlobalBounds.Left,
                        mapping.GlobalBounds.Top,
                        selection.Width,
                        selection.Height),
                    capturedAt),
                cancellationToken).ConfigureAwait(false);

            // Persistence is the commit point. A failed output action must not trigger a fallback
            // that would create a duplicate shot from the original frozen frame.
            return new HighResolutionCaptureOutcome(
                HighResolutionCaptureStatus.Stored,
                HighResolutionCaptureContinuation.CompleteSession,
                stored,
                actionExecution,
                selection.Width,
                selection.Height);
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            return new HighResolutionCaptureOutcome(
                HighResolutionCaptureStatus.Canceled,
                HighResolutionCaptureContinuation.CompleteSession,
                Error: error);
        }
        catch (Exception error)
        {
            return new HighResolutionCaptureOutcome(
                HighResolutionCaptureStatus.Failed,
                FailureContinuation(request.Mode),
                Error: error);
        }
    }

    private static HighResolutionCaptureContinuation FailureContinuation(
        HighResolutionCaptureMode mode) => mode switch
        {
            HighResolutionCaptureMode.Automatic =>
                HighResolutionCaptureContinuation.ExecuteFrozenDecision,
            HighResolutionCaptureMode.Explicit =>
                HighResolutionCaptureContinuation.ResumeFrozenSelection,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
}
