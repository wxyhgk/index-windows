using Index.Actions;
using Index.Annotation;
using Index.Capture;
using Index.Pin;

namespace Index.Tests;

public sealed class PinActionSessionTests
{
    [Fact]
    public async Task ExecuteAsync_BuildsStablePinnedContextWithInjectedHost()
    {
        var action = new RecordingAction("record");
        var registry = Registry(action);
        var artifact = new CaptureArtifact([1, 2, 3], new Layers<ImageSpace>());
        var region = new CaptureRegion(10, 20, 30, 40);
        var host = new RecordingHost();
        var session = new PinActionSession(
            registry,
            artifact,
            region,
            "Index_test.png",
            host);

        var result = await session.ExecuteAsync(action.Descriptor.Id);

        Assert.Equal(CaptureActionExecutionStatus.Completed, result.Status);
        Assert.NotNull(action.Context);
        Assert.Same(artifact, action.Context.Artifact);
        Assert.Equal(region, action.Context.Region);
        Assert.Equal("Index_test.png", action.Context.SuggestedFileName);
        Assert.Same(host, action.Context.Host);
    }

    [Fact]
    public async Task ExecutionState_IsOwnedBySessionAndRejectsDuplicateCommand()
    {
        var action = new BlockingAction("blocking");
        var session = new PinActionSession(
            Registry(action),
            new CaptureArtifact([1], new Layers<ImageSpace>()),
            region: null,
            suggestedFileName: null,
            new RecordingHost());
        var stateChanges = new List<(string Id, bool IsExecuting)>();
        session.ExecutionStateChanged += id => stateChanges.Add((id, session.IsExecuting(id)));

        var first = session.ExecuteAsync(action.Descriptor.Id);
        await action.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(session.IsExecuting(action.Descriptor.Id));
        var duplicate = await session.ExecuteAsync(action.Descriptor.Id);
        Assert.Equal(CaptureActionExecutionStatus.AlreadyExecuting, duplicate.Status);

        action.Release();
        var completed = await first;

        Assert.Equal(CaptureActionExecutionStatus.Completed, completed.Status);
        Assert.False(session.IsExecuting(action.Descriptor.Id));
        Assert.Equal(
            [(action.Descriptor.Id, true), (action.Descriptor.Id, false)],
            stateChanges);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNotFoundForUnknownCommand()
    {
        var session = new PinActionSession(
            new CaptureActionRegistry(),
            new CaptureArtifact([1], new Layers<ImageSpace>()),
            region: null,
            suggestedFileName: null,
            new RecordingHost());

        var result = await session.ExecuteAsync("missing");

        Assert.Equal(CaptureActionExecutionStatus.NotFound, result.Status);
    }

    private static CaptureActionRegistry Registry(ICaptureAction action)
    {
        var registry = new CaptureActionRegistry();
        registry.Register(action);
        return registry;
    }

    private sealed class RecordingAction(string id) : ICaptureAction
    {
        public CaptureActionDescriptor Descriptor { get; } = new(
            id,
            "Record",
            "R",
            new HashSet<CaptureActionScope> { CaptureActionScope.Pinned });

        public CaptureContext? Context { get; private set; }

        public ValueTask PerformAsync(
            CaptureContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingAction(string id) : ICaptureAction
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public CaptureActionDescriptor Descriptor { get; } = new(
            id,
            "Blocking",
            "B",
            new HashSet<CaptureActionScope> { CaptureActionScope.Pinned });

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask PerformAsync(
            CaptureContext context,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingHost : ICaptureActionHost
    {
        public void Dismiss() { }
    }
}
