namespace Index.Capture;

/// <summary>
/// Keeps the currently running high-resolution transition reachable so shutdown
/// can wait for window and display leases to finish restoring.
/// </summary>
public sealed class CaptureTransitionTaskTracker
{
    private readonly object _gate = new();
    private Task? _active;
    private bool _closed;

    public bool HasActiveTransition
    {
        get
        {
            lock (_gate)
            {
                return _active is { IsCompleted: false };
            }
        }
    }

    public Task CurrentTransition
    {
        get
        {
            lock (_gate)
            {
                return _active ?? Task.CompletedTask;
            }
        }
    }

    public Task Track(Func<Task> startTransition)
    {
        if (!TryTrack(startTransition, out var transition))
            throw new InvalidOperationException("The capture transition tracker is closed.");
        return transition;
    }

    public bool TryTrack(Func<Task> startTransition, out Task transition)
    {
        ArgumentNullException.ThrowIfNull(startTransition);

        lock (_gate)
        {
            if (_closed)
            {
                transition = Task.CompletedTask;
                return false;
            }

            transition = startTransition()
                ?? throw new InvalidOperationException(
                    "The capture transition factory returned no task.");
            _active = transition;
        }

        _ = ClearWhenCompletedAsync(transition);
        return true;
    }

    /// <summary>
    /// Atomically prevents later transitions and snapshots the task whose platform lease must
    /// finish restoring. Callers may safely invoke this more than once.
    /// </summary>
    public Task CloseAndGetCurrent()
    {
        lock (_gate)
        {
            _closed = true;
            return _active ?? Task.CompletedTask;
        }
    }

    public async Task<bool> WaitForCompletionAsync(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        Task? transition;
        lock (_gate)
        {
            transition = _active;
        }

        if (transition is null)
            return true;

        try
        {
            await transition.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch
        {
            // Completion, rather than success, is what lease-safe shutdown needs.
            // The transition owner remains responsible for logging its failure.
            return true;
        }
    }

    private async Task ClearWhenCompletedAsync(Task transition)
    {
        try
        {
            await transition.ConfigureAwait(false);
        }
        catch
        {
            // The transition owner observes and reports the original failure.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_active, transition))
                    _active = null;
            }
        }
    }
}
