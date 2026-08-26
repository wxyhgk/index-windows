namespace Index.Recognition;

public enum RecognitionServiceHostState
{
    Stopped,
    Checking,
    Starting,
    Ready,
    Failed,
    Disposed
}

public sealed record RecognitionServiceHostStatus(
    RecognitionServiceHostState State,
    string Message,
    bool OwnsProcess = false,
    int? ProcessId = null,
    string? ProtocolVersion = null)
{
    public bool IsReady => State == RecognitionServiceHostState.Ready;
}

public interface IRecognitionServiceHost : IDisposable
{
    RecognitionServiceHostStatus Status { get; }

    Task<RecognitionServiceHostStatus> EnsureReadyAsync(
        CancellationToken cancellationToken = default);

    void NotifyActivityCompleted()
    {
    }
}
