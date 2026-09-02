using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Storage;

namespace Index.Tests;

public sealed class VirtualWindowCaptureWorkflowTests
{
    private static readonly nint TargetWindow = (nint)0x1234;
    private static readonly SourceWindowBounds OriginalBounds = new(100, 80, 1380, 800);

    [Fact]
    public async Task LocksForegroundWindowAndPersistsOriginalBounds()
    {
        var source = new FakeFrameSource(Frame());
        var resolver = new FakeSourceResolver(TargetWindow);
        var persistence = new FakePersistence();
        var workflow = CreateWorkflow(source, resolver, persistence);

        var outcome = await workflow.CaptureForegroundAndSaveAsync();

        Assert.True(outcome.IsSuccess);
        Assert.Equal(TargetWindow, source.LastTarget?.WindowHandle);
        Assert.Equal((uint)42, source.LastTarget?.ProcessId);
        Assert.Equal(OriginalBounds, source.LastTarget?.OriginalBounds);
        Assert.Equal(3840, outcome.Width);
        Assert.Equal(2160, outcome.Height);
        Assert.NotNull(persistence.LastRequest);
        Assert.Equal(OriginalBounds, persistence.LastRequest.GlobalRegion);
        Assert.Same(resolver.Snapshot, persistence.LastRequest.SourceSnapshot);
        Assert.Equal(3840, persistence.LastRequest.Selection.Width);
        Assert.Equal(2160, persistence.LastRequest.Selection.Height);
    }

    [Fact]
    public async Task MissingForegroundWindowDoesNotMutateDisplayOrPersist()
    {
        var source = new FakeFrameSource(Frame());
        var persistence = new FakePersistence();
        var workflow = CreateWorkflow(
            source,
            new FakeSourceResolver(nint.Zero),
            persistence);

        var outcome = await workflow.CaptureForegroundAndSaveAsync();

        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Unavailable, outcome.Kind);
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal(0, persistence.SaveCount);
    }

    [Fact]
    public async Task CancellationIsStructuredAndDoesNotPersist()
    {
        var source = new FakeFrameSource(Frame(), waitForCancellation: true);
        var persistence = new FakePersistence();
        var workflow = CreateWorkflow(
            source,
            new FakeSourceResolver(TargetWindow),
            persistence);
        using var cancellation = new CancellationTokenSource();

        var capture = workflow.CaptureForegroundAndSaveAsync(cancellation.Token);
        cancellation.Cancel();
        var outcome = await capture;

        Assert.Equal(VirtualDisplayCaptureOutcomeKind.Canceled, outcome.Kind);
        Assert.Equal(0, persistence.SaveCount);
    }

    private static VirtualWindowCaptureWorkflow CreateWorkflow(
        FakeFrameSource source,
        FakeSourceResolver resolver,
        FakePersistence persistence) => new(
            source,
            resolver,
            new FakeImagePreparer(),
            persistence);

    private static VirtualWindowCaptureFrame Frame() => new(
        new DisplaySnapshot
        {
            DisplayId = @"MONITOR\MTT1337\0001",
            DeviceName = "VDD by MTT · 4K window",
            DisplayIndex = 0,
            IsPrimary = false,
            Width = 3840,
            Height = 2160,
            DpiScale = 1,
            Left = 1920,
            Top = 0,
            PngData = [1, 2, 3]
        },
        OriginalBounds);

    private sealed class FakeFrameSource : IVirtualWindowFrameSource
    {
        private readonly VirtualWindowCaptureFrame _frame;
        private readonly bool _waitForCancellation;

        public FakeFrameSource(
            VirtualWindowCaptureFrame frame,
            bool waitForCancellation = false)
        {
            _frame = frame;
            _waitForCancellation = waitForCancellation;
        }

        public int CaptureCount { get; private set; }
        public VirtualWindowCaptureTarget? LastTarget { get; private set; }

        public VirtualDisplayCaptureStatus GetStatus() => new(
            VirtualDisplayAvailability.Active,
            "VDD by MTT",
            3840,
            2160);

        public async Task<VirtualWindowCaptureFrame> CaptureAsync(
            VirtualWindowCaptureTarget target,
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            LastTarget = target;
            if (_waitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return _frame;
        }
    }

    private sealed class FakeSourceResolver : ISourceApplicationResolver
    {
        public FakeSourceResolver(nint foregroundWindowHandle)
        {
            Snapshot = new SourceApplicationSnapshot(
                new SourceApplicationInfo(42, "Target", "target.exe", "Target window"),
                foregroundWindowHandle == nint.Zero
                    ? []
                    : [new SourceWindowInfo(
                        foregroundWindowHandle,
                        42,
                        "Target window",
                        OriginalBounds,
                        foregroundWindowHandle,
                        HierarchyDepth: 0)],
                foregroundWindowHandle);
        }

        public SourceApplicationSnapshot Snapshot { get; }

        public SourceApplicationSnapshot CaptureSnapshot() => Snapshot;

        public SourceApplicationInfo? Resolve(
            SourceApplicationSnapshot snapshot,
            SourceWindowBounds region) => snapshot.ForegroundApplication;
    }

    private sealed class FakeImagePreparer : ICaptureImagePreparer
    {
        public PreparedCaptureImage PrepareFrozen(
            CaptureSelection selection,
            ReadOnlyMemory<byte> frozenPng) => new(
                frozenPng.ToArray(),
                new CaptureArtifact(frozenPng.ToArray(), selection.Layers));

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
