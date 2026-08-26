using Index.Capture;
using Index.Platform;
using Index.UI.Editor;
using Microsoft.UI.Dispatching;

namespace Index.App;

/// <summary>
/// Describes work selected by the coordinator without exposing overlay lifetime mechanics to it.
/// Non-deferred work starts after the overlay is released; deferred work keeps the frozen frame
/// visible until the operation completes.
/// </summary>
internal readonly record struct CaptureOverlayDecisionHandling(
    bool DeferReleaseUntilCompleted,
    Func<Task> Execute);

/// <summary>
/// Owns one selection overlay's event subscriptions and UI-thread release sequence.
/// </summary>
internal sealed class CaptureOverlaySessionController : IDisposable
{
    private readonly SelectionOverlaySession _session;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<SelectionOverlayDecision, CaptureOverlayDecisionHandling> _handleDecision;
    private readonly Action<CaptureOverlaySessionController> _released;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _isReleased;

    public CaptureOverlaySessionController(
        SelectionOverlaySession session,
        DispatcherQueue dispatcher,
        Func<SelectionOverlayDecision, CaptureOverlayDecisionHandling> handleDecision,
        Action<CaptureOverlaySessionController> released,
        Action<string> log)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handleDecision = handleDecision ?? throw new ArgumentNullException(nameof(handleDecision));
        _released = released ?? throw new ArgumentNullException(nameof(released));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // The controller, rather than SelectionOverlaySession, decides when the frozen frame can
        // disappear. Pinning relies on this frame as its handoff cover.
        _session.DismissOnCapture = false;
        _session.CaptureRequested += OnCaptureRequested;
        _session.PixelEdgeDetectionRequested += OnPixelEdgeDetectionRequested;
        _session.Canceled += OnCanceled;
    }

    public event Action<DisplaySnapshot>? PixelEdgeDetectionRequested;

    public CancellationToken LifetimeToken => _lifetimeCancellation.Token;

    public void Show() => _session.Show();

    public bool TryReactivate() => _session.TryReactivate();

    public bool TrySetPixelEdgeDetector(
        string displayId,
        FrozenPixelEdgeDetector? detector)
    {
        if (Volatile.Read(ref _isReleased) != 0)
            return false;

        _session.SetPixelEdgeDetector(displayId, detector);
        return true;
    }

    public void Cancel() => _session.Cancel();

    public void Dispose() => Release();

    private void OnCaptureRequested(SelectionOverlayDecision decision)
    {
        var handling = _handleDecision(decision);
        if (handling.DeferReleaseUntilCompleted)
        {
            _ = ExecuteThenReleaseAsync(handling.Execute);
            return;
        }

        // Preserve the normal capture sequence: dismiss the overlay before action preparation.
        Release();
        _ = handling.Execute();
    }

    private void OnCanceled() => Release();

    private void OnPixelEdgeDetectionRequested(DisplaySnapshot snapshot)
        => PixelEdgeDetectionRequested?.Invoke(snapshot);

    private async Task ExecuteThenReleaseAsync(Func<Task> execute)
    {
        try
        {
            await execute().ConfigureAwait(false);
        }
        finally
        {
            // Window lifetime belongs to the UI thread. For pinning, Execute returns only after
            // the movable surface has been presented, preventing a visible handoff gap.
            if (!_dispatcher.TryEnqueue(Release))
                _log("Unable to release overlay after pin presentation");
        }
    }

    private void Release()
    {
        if (Interlocked.Exchange(ref _isReleased, 1) != 0)
            return;

        _lifetimeCancellation.Cancel();
        _session.CaptureRequested -= OnCaptureRequested;
        _session.PixelEdgeDetectionRequested -= OnPixelEdgeDetectionRequested;
        _session.Canceled -= OnCanceled;
        PixelEdgeDetectionRequested = null;
        _session.Dispose();
        _lifetimeCancellation.Dispose();
        _released(this);
    }
}
