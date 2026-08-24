using Index.Actions;
using Index.Capture;

namespace Index.Pin;

/// <summary>
/// Owns the immutable action context and execution state for one pinned capture.
/// UI hosts only provide their lifecycle boundary and forward command identifiers.
/// </summary>
public sealed class PinActionSession
{
    private readonly CaptureActionExecutor _executor;
    private readonly CaptureContext _context;

    public PinActionSession(
        CaptureActionRegistry actions,
        CaptureArtifact artifact,
        CaptureRegion? region,
        string? suggestedFileName,
        ICaptureActionHost host)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(host);

        _executor = new CaptureActionExecutor(actions);
        _context = new CaptureContext
        {
            Artifact = artifact,
            Region = region,
            SuggestedFileName = suggestedFileName,
            Host = host
        };
    }

    public event Action<string>? ExecutionStateChanged
    {
        add => _executor.ExecutionStateChanged += value;
        remove => _executor.ExecutionStateChanged -= value;
    }

    public bool IsExecuting(string id) => _executor.IsExecuting(id);

    public Task<CaptureActionExecutionResult> ExecuteAsync(
        string id,
        CancellationToken cancellationToken = default)
        => _executor.ExecuteAsync(id, _context, cancellationToken);
}
