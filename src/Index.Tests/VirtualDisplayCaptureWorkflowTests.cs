using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Storage;

namespace Index.Tests;

public sealed class VirtualDisplayCaptureWorkflowTests
{
    [Fact]
    public async Task ReturnsUnavailableWithoutStartingCapture()
    {
        var source = new FakeFrameSource(
            new VirtualDisplayCaptureStatus(VirtualDisplayAvailability.NotInstalled));
        var workflow = CreateWorkflow(source, out var persistence);

        var outcome = await workflow.CaptureAndSaveAsync();

        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Unavailable, outcome.Kind);
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal(0, persistence.SaveCount);
    }

    [Fact]
    public async Task SavesTheCompleteVirtualDisplayFrame()
    {
        var source = new FakeFrameSource(
            new VirtualDisplayCaptureStatus(
                VirtualDisplayAvailability.Active,
                "VDD by MTT",
                3840,
                2160),
            Snapshot());
        var workflow = CreateWorkflow(source, out var persistence);

        var outcome = await workflow.CaptureAndSaveAsync();

        Assert.True(outcome.IsSuccess);
        Assert.Equal(3840, outcome.Width);
        Assert.Equal(2160, outcome.Height);
        Assert.Equal(1, persistence.SaveCount);
        Assert.NotNull(persistence.LastRequest);
        Assert.Equal(0, persistence.LastRequest.Selection.X);
        Assert.Equal(0, persistence.LastRequest.Selection.Y);
        Assert.Equal(3840, persistence.LastRequest.Selection.Width);
        Assert.Equal(2160, persistence.LastRequest.Selection.Height);
        Assert.Equal("VDD by MTT", persistence.LastRequest.Display.DeviceName);
    }

    [Fact]
    public async Task CancellationIsStructuredAndDoesNotPersist()
    {
        var source = new FakeFrameSource(
            new VirtualDisplayCaptureStatus(
                VirtualDisplayAvailability.Active,
                "VDD by MTT",
                3840,
                2160),
            Snapshot(),
            cancelDuringCapture: true);
        var workflow = CreateWorkflow(source, out var persistence);
        using var cancellation = new CancellationTokenSource();

        var capture = workflow.CaptureAndSaveAsync(cancellation.Token);
        cancellation.Cancel();
        var outcome = await capture;

        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Canceled, outcome.Kind);
        Assert.Equal(0, persistence.SaveCount);
    }

    [Fact]
    public async Task WaitForCompletionIncludesLeaseRestoreAfterCancellation()
    {
        var source = new RestoringFrameSource();
        var workflow = new VirtualDisplayCaptureWorkflow(
            source,
            new FakeSourceResolver(),
            new FakeImagePreparer(),
            new FakePersistence());
        using var cancellation = new CancellationTokenSource();

        var capture = workflow.CaptureAndSaveAsync(cancellation.Token);
        await source.CaptureStarted.Task;
        cancellation.Cancel();
        await source.RestoreStarted.Task;

        Assert.False(await workflow.WaitForCompletionAsync(
            TimeSpan.FromMilliseconds(10)));
        var busy = await workflow.CaptureAndSaveAsync();
        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Busy, busy.Kind);

        source.AllowRestore.SetResult();
        Assert.True(await workflow.WaitForCompletionAsync(TimeSpan.FromSeconds(1)));
        var outcome = await capture;
        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Canceled, outcome.Kind);
    }

    [Fact]
    public async Task BeginShutdownCancelsCaptureAndExposesLeaseRestoration()
    {
        var source = new RestoringFrameSource();
        var workflow = new VirtualDisplayCaptureWorkflow(
            source,
            new FakeSourceResolver(),
            new FakeImagePreparer(),
            new FakePersistence());

        var capture = workflow.CaptureAndSaveAsync();
        await source.CaptureStarted.Task;

        var shutdown = workflow.BeginShutdown();
        await source.RestoreStarted.Task;

        Assert.Equal("virtual-display", shutdown.Name);
        Assert.False(shutdown.Restoration.IsCompleted);
        var rejected = await workflow.CaptureAndSaveAsync();
        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Canceled, rejected.Kind);

        source.AllowRestore.SetResult();
        await shutdown.Restoration;
        var outcome = await capture;
        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Canceled, outcome.Kind);
    }

    [Fact]
    public async Task ShutdownRestorationDoesNotWaitForPersistence()
    {
        var persistence = new BlockingPersistence();
        var workflow = new VirtualDisplayCaptureWorkflow(
            new FakeFrameSource(
                new VirtualDisplayCaptureStatus(
                    VirtualDisplayAvailability.Active,
                    "VDD by MTT",
                    3840,
                    2160),
                Snapshot()),
            new FakeSourceResolver(),
            new FakeImagePreparer(),
            persistence);

        var capture = workflow.CaptureAndSaveAsync();
        await persistence.Started.Task;

        var shutdown = workflow.BeginShutdown();
        await shutdown.Restoration;

        Assert.False(capture.IsCompleted);
        persistence.AllowSave.SetResult();
        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Success, (await capture).Kind);
    }

    [Fact]
    public async Task ShutdownRestorationSurfacesFrameScopeFailure()
    {
        var source = new FailingFrameSource();
        var workflow = new VirtualDisplayCaptureWorkflow(
            source,
            new FakeSourceResolver(),
            new FakeImagePreparer(),
            new FakePersistence());

        var capture = workflow.CaptureAndSaveAsync();
        await source.Started.Task;
        var shutdown = workflow.BeginShutdown();
        source.AllowFailure.SetResult();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => shutdown.Restoration);
        Assert.Equal("restore failed", error.Message);
        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Failed, (await capture).Kind);
    }

    private static VirtualDisplayCaptureWorkflow CreateWorkflow(
        FakeFrameSource source,
        out FakePersistence persistence)
    {
        persistence = new FakePersistence();
        return new VirtualDisplayCaptureWorkflow(
            source,
            new FakeSourceResolver(),
            new FakeImagePreparer(),
            persistence);
    }

    private static DisplaySnapshot Snapshot() => new()
    {
        DisplayId = @"MONITOR\MTT1337\0001",
        DeviceName = "VDD by MTT",
        DisplayIndex = 0,
        IsPrimary = false,
        Width = 3840,
        Height = 2160,
        DpiScale = 1,
        Left = 1920,
        Top = 0,
        PngData = [1, 2, 3]
    };

    private sealed class FakeFrameSource : IVirtualDisplayFrameSource
    {
        private readonly VirtualDisplayCaptureStatus _status;
        private readonly DisplaySnapshot? _snapshot;
        private readonly bool _cancelDuringCapture;

        public FakeFrameSource(
            VirtualDisplayCaptureStatus status,
            DisplaySnapshot? snapshot = null,
            bool cancelDuringCapture = false)
        {
            _status = status;
            _snapshot = snapshot;
            _cancelDuringCapture = cancelDuringCapture;
        }

        public int CaptureCount { get; private set; }

        public VirtualDisplayCaptureStatus GetStatus() => _status;

        public async Task<DisplaySnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            if (_cancelDuringCapture)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return _snapshot ?? throw new InvalidOperationException("Missing fake frame.");
        }
    }

    private sealed class RestoringFrameSource : IVirtualDisplayFrameSource
    {
        public TaskCompletionSource CaptureStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RestoreStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowRestore { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public VirtualDisplayCaptureStatus GetStatus() => new(
            VirtualDisplayAvailability.Active,
            "VDD by MTT",
            3840,
            2160);

        public async Task<DisplaySnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            CaptureStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                RestoreStarted.TrySetResult();
                await AllowRestore.Task;
            }

            throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
        }
    }

    private sealed class FailingFrameSource : IVirtualDisplayFrameSource
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowFailure { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public VirtualDisplayCaptureStatus GetStatus() => new(
            VirtualDisplayAvailability.Active,
            "VDD by MTT",
            3840,
            2160);

        public async Task<DisplaySnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await AllowFailure.Task;
            throw new InvalidOperationException("restore failed");
        }
    }

    private sealed class FakeSourceResolver : ISourceApplicationResolver
    {
        public SourceApplicationSnapshot CaptureSnapshot() => new(null, []);

        public SourceApplicationInfo? Resolve(
            SourceApplicationSnapshot snapshot,
            SourceWindowBounds region) => null;
    }

    private sealed class FakeImagePreparer : ICaptureImagePreparer
    {
        public PreparedCaptureImage PrepareFrozen(
            CaptureSelection selection,
            ReadOnlyMemory<byte> frozenPng) =>
            new(frozenPng.ToArray(), new CaptureArtifact(
                frozenPng.ToArray(),
                selection.Layers));

        public PreparedCaptureImage? TryPrepareDirect(
            CaptureSelection selection,
            byte[] directPng) => PrepareFrozen(selection, directPng);
    }

    private sealed class FakePersistence : ICapturePersistenceService
    {
        public int SaveCount { get; private set; }
        public CapturePersistenceRequest? LastRequest { get; private set; }

        public Task<StoredCapture> SaveAsync(
            CapturePersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastRequest = request;
            return Task.FromResult(new StoredCapture(
                new ShotRecord(
                    1,
                    "sha",
                    DateTimeOffset.UtcNow,
                    request.Selection.Width,
                    request.Selection.Height,
                    1,
                    null,
                    null,
                    null,
                    null,
                    0,
                    request.Display.DeviceName,
                    request.GlobalRegion.Left,
                    request.GlobalRegion.Top,
                    request.Selection.Width,
                    request.Selection.Height,
                    "png"),
                new RevisionRecord(1, 1, null, DateTimeOffset.UtcNow, null, "[]")));
        }
    }

    private sealed class BlockingPersistence : ICapturePersistenceService
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSave { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StoredCapture> SaveAsync(
            CapturePersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await AllowSave.Task;
            return new StoredCapture(
                new ShotRecord(
                    1,
                    "sha",
                    DateTimeOffset.UtcNow,
                    request.Selection.Width,
                    request.Selection.Height,
                    1,
                    null,
                    null,
                    null,
                    null,
                    0,
                    request.Display.DeviceName,
                    request.GlobalRegion.Left,
                    request.GlobalRegion.Top,
                    request.Selection.Width,
                    request.Selection.Height,
                    "png"),
                new RevisionRecord(1, 1, null, DateTimeOffset.UtcNow, null, "[]"));
        }
    }
}
