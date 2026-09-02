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
}
