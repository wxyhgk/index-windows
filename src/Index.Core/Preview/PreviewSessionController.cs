namespace Index.Preview;

/// <summary>
/// An immutable identity and cancellation snapshot for one previewed shot.
/// </summary>
public sealed record PreviewSession(
    Guid SessionId,
    long ShotId,
    long Generation,
    CancellationToken CancellationToken);

/// <summary>
/// Owns the lifetime of the active preview session. Starting, deactivating, or
/// disposing a preview invalidates and cancels the previous session.
/// </summary>
public sealed class PreviewSessionController : IDisposable
{
    private readonly object _gate = new();
    private SessionState? _current;
    private long _generation;
    private bool _disposed;

    public PreviewSession Begin(long shotId)
    {
        SessionState? previous;
        PreviewSession session;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            previous = _current;
            var cancellation = new CancellationTokenSource();
            session = new PreviewSession(
                Guid.NewGuid(),
                shotId,
                checked(++_generation),
                cancellation.Token);
            _current = new SessionState(session, cancellation);
        }

        CancelAndDispose(previous);
        return session;
    }

    public bool IsCurrent(PreviewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_gate)
        {
            return !_disposed
                && _current is { } current
                && !current.Cancellation.IsCancellationRequested
                && current.Session.SessionId == session.SessionId
                && current.Session.ShotId == session.ShotId
                && current.Session.Generation == session.Generation;
        }
    }

    public void Deactivate()
    {
        SessionState? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _current;
            _current = null;
            checked { ++_generation; }
        }

        CancelAndDispose(previous);
    }

    public void Dispose()
    {
        SessionState? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            previous = _current;
            _current = null;
            checked { ++_generation; }
        }

        CancelAndDispose(previous);
    }

    private static void CancelAndDispose(SessionState? state)
    {
        if (state is null)
        {
            return;
        }

        state.Cancellation.Cancel();
        state.Cancellation.Dispose();
    }

    private sealed record SessionState(
        PreviewSession Session,
        CancellationTokenSource Cancellation);
}
