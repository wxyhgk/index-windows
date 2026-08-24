namespace Index.Actions;

public enum CaptureActionExecutionStatus
{
    Completed,
    Canceled,
    AlreadyExecuting,
    NotFound,
    Failed
}

public sealed record CaptureActionExecutionResult(
    CaptureActionExecutionStatus Status,
    Exception? Error = null)
{
    public bool IsSuccess => Status == CaptureActionExecutionStatus.Completed;
}

/// <summary>统一解析、去重和观察异步动作执行。</summary>
public sealed class CaptureActionExecutor
{
    private readonly CaptureActionRegistry _registry;
    private readonly object _gate = new();
    private readonly HashSet<string> _executingIds = new(StringComparer.Ordinal);

    public CaptureActionExecutor(CaptureActionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public event Action<string>? ExecutionStateChanged;

    public bool IsExecuting(string id)
    {
        lock (_gate)
            return _executingIds.Contains(id);
    }

    public async Task<CaptureActionExecutionResult> ExecuteAsync(
        string id,
        CaptureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(context);

        var action = _registry.Find(id);
        if (action is null)
            return new CaptureActionExecutionResult(CaptureActionExecutionStatus.NotFound);

        lock (_gate)
        {
            if (!_executingIds.Add(id))
                return new CaptureActionExecutionResult(CaptureActionExecutionStatus.AlreadyExecuting);
        }
        NotifyExecutionStateChanged(id);

        try
        {
            await action.PerformAsync(context, cancellationToken).ConfigureAwait(false);
            return new CaptureActionExecutionResult(CaptureActionExecutionStatus.Completed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CaptureActionExecutionResult(CaptureActionExecutionStatus.Canceled);
        }
        catch (Exception error)
        {
            return new CaptureActionExecutionResult(CaptureActionExecutionStatus.Failed, error);
        }
        finally
        {
            lock (_gate)
                _executingIds.Remove(id);
            NotifyExecutionStateChanged(id);
        }
    }

    private void NotifyExecutionStateChanged(string id)
    {
        if (ExecutionStateChanged is not { } handlers) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(id);
            }
            catch
            {
                // 观察者不能破坏动作执行或让 busy 状态永久残留。
            }
        }
    }
}
