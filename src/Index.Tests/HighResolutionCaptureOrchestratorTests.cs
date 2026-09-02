using Index.Actions;
using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Storage;

namespace Index.Tests;

public sealed class HighResolutionCaptureOrchestratorTests
{
    private static readonly nint TargetWindow = (nint)0x1234;
    private static readonly SourceWindowBounds OriginalWindowBounds = new(100, 200, 1300, 1000);

    [Theory]
    [InlineData(HighResolutionCaptureMode.Explicit, CaptureActionIds.Copy, CaptureActionIds.Complete)]
    [InlineData(HighResolutionCaptureMode.Automatic, CaptureActionIds.Copy, CaptureActionIds.Copy)]
    public async Task StoresMappedSelectionAndExecutesModeAction(
        HighResolutionCaptureMode mode,
        string requestedActionId,
        string expectedActionId)
    {
        var frameSource = new FakeFrameSource(Frame());
        var persistence = new FakePersistence();
        var actions = new CaptureActionRegistry();
        var complete = new RecordingAction(CaptureActionIds.Complete);
        var copy = new RecordingAction(CaptureActionIds.Copy);
        actions.Register(complete);
        actions.Register(copy);
        var orchestrator = CreateOrchestrator(frameSource, persistence, actions);
        var sourceSnapshot = SourceSnapshot();
        var sourceLayers = new Layers<ImageSpace>();
        sourceLayers.Append(new Layer(
            LayerKind.Rect,
            new LRect(150, 120, 200, 100),
            new LColor(1, 0, 0, 1),
            3,
            fontSize: 12));

        var outcome = await orchestrator.ExecuteAsync(Request(
            mode,
            requestedActionId,
            sourceSnapshot,
            sourceLayers));

        Assert.Equal(HighResolutionCaptureStatus.Stored, outcome.Status);
        Assert.Equal(HighResolutionCaptureContinuation.CompleteSession, outcome.Continuation);
        Assert.Equal(1200, outcome.Width);
        Assert.Equal(800, outcome.Height);
        Assert.Equal(TargetWindow, frameSource.LastTarget?.WindowHandle);
        Assert.Equal((uint)42, frameSource.LastTarget?.ProcessId);
        Assert.Equal(OriginalWindowBounds, frameSource.LastTarget?.OriginalBounds);
        var saved = Assert.IsType<CapturePersistenceRequest>(persistence.LastRequest);
        Assert.Same(sourceSnapshot, saved.SourceSnapshot);
        Assert.Equal(new SourceWindowBounds(400, 400, 1000, 800), saved.GlobalRegion);
        Assert.Equal(600, saved.Selection.X);
        Assert.Equal(400, saved.Selection.Y);
        Assert.Equal(1200, saved.Selection.Width);
        Assert.Equal(800, saved.Selection.Height);
        var mappedLayer = Assert.Single(saved.Selection.Layers.Elements);
        Assert.Equal(new LRect(300, 240, 400, 200), mappedLayer.Rect);
        Assert.Equal(6, mappedLayer.LineWidth);
        Assert.Equal(24, mappedLayer.FontSize);
        var invoked = expectedActionId == CaptureActionIds.Complete ? complete : copy;
        Assert.Equal(1, invoked.ExecutionCount);
        Assert.Equal(
            new CaptureRegion(400, 400, 1200, 800),
            Assert.IsType<CaptureRegion>(invoked.LastContext?.Region));
        Assert.Equal(0, (expectedActionId == CaptureActionIds.Complete ? copy : complete).ExecutionCount);
    }

    [Theory]
    [InlineData(HighResolutionCaptureMode.Explicit, HighResolutionCaptureContinuation.ResumeFrozenSelection)]
    [InlineData(HighResolutionCaptureMode.Automatic, HighResolutionCaptureContinuation.ExecuteFrozenDecision)]
    public async Task CaptureFailureReturnsModeSpecificContinuation(
        HighResolutionCaptureMode mode,
        HighResolutionCaptureContinuation expectedContinuation)
    {
        var frameSource = new FakeFrameSource(
            Frame(),
            error: new InvalidOperationException("capture failed"));
        var persistence = new FakePersistence();
        var actions = Actions(new RecordingAction(CaptureActionIds.Complete));
        var orchestrator = CreateOrchestrator(frameSource, persistence, actions);

        var outcome = await orchestrator.ExecuteAsync(Request(mode));

        Assert.Equal(HighResolutionCaptureStatus.Failed, outcome.Status);
        Assert.Equal(expectedContinuation, outcome.Continuation);
        Assert.IsType<InvalidOperationException>(outcome.Error);
        Assert.Equal(0, persistence.SaveCount);
    }

    [Theory]
    [InlineData(HighResolutionCaptureMode.Explicit, HighResolutionCaptureContinuation.ResumeFrozenSelection)]
    [InlineData(HighResolutionCaptureMode.Automatic, HighResolutionCaptureContinuation.ExecuteFrozenDecision)]
    public async Task PersistenceFailureReturnsModeSpecificContinuation(
        HighResolutionCaptureMode mode,
        HighResolutionCaptureContinuation expectedContinuation)
    {
        var persistence = new FakePersistence(new IOException("disk failed"));
        var action = new RecordingAction(CaptureActionIds.Complete);
        var orchestrator = CreateOrchestrator(
            new FakeFrameSource(Frame()),
            persistence,
            Actions(action));

        var outcome = await orchestrator.ExecuteAsync(Request(mode));

        Assert.Equal(HighResolutionCaptureStatus.Failed, outcome.Status);
        Assert.Equal(expectedContinuation, outcome.Continuation);
        Assert.IsType<IOException>(outcome.Error);
        Assert.Equal(1, persistence.SaveCount);
        Assert.Equal(0, action.ExecutionCount);
    }

    [Fact]
    public async Task CancellationIsStructuredAndDoesNotFallBack()
    {
        var frameSource = new FakeFrameSource(Frame(), waitForCancellation: true);
        var persistence = new FakePersistence();
        var orchestrator = CreateOrchestrator(
            frameSource,
            persistence,
            Actions(new RecordingAction(CaptureActionIds.Copy)));
        using var cancellation = new CancellationTokenSource();

        var capture = orchestrator.ExecuteAsync(
            Request(HighResolutionCaptureMode.Automatic),
            cancellation.Token);
        cancellation.Cancel();
        var outcome = await capture;

        Assert.Equal(HighResolutionCaptureStatus.Canceled, outcome.Status);
        Assert.Equal(HighResolutionCaptureContinuation.CompleteSession, outcome.Continuation);
        Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
        Assert.Equal(0, persistence.SaveCount);
    }

    [Fact]
    public async Task FailedActionRemainsStoredAndDoesNotFallBack()
    {
        var persistence = new FakePersistence();
        var failingCopy = new RecordingAction(
            CaptureActionIds.Copy,
            new InvalidOperationException("clipboard failed"));
        var orchestrator = CreateOrchestrator(
            new FakeFrameSource(Frame()),
            persistence,
            Actions(failingCopy));

        var outcome = await orchestrator.ExecuteAsync(Request(HighResolutionCaptureMode.Automatic));

        Assert.Equal(HighResolutionCaptureStatus.Stored, outcome.Status);
        Assert.Equal(HighResolutionCaptureContinuation.CompleteSession, outcome.Continuation);
        Assert.Equal(1, persistence.SaveCount);
        Assert.Equal(CaptureActionExecutionStatus.Failed, outcome.ActionExecution?.Status);
        Assert.IsType<InvalidOperationException>(outcome.ActionExecution?.Error);
    }

    private static HighResolutionCaptureOrchestrator CreateOrchestrator(
        FakeFrameSource frameSource,
        FakePersistence persistence,
        CaptureActionRegistry actions) => new(
            frameSource,
            new FakeImagePreparer(),
            persistence,
            new CaptureActionExecutor(actions),
            new CaptureActionContextFactory());

    private static CaptureActionRegistry Actions(params ICaptureAction[] actions)
    {
        var registry = new CaptureActionRegistry();
        foreach (var action in actions)
            registry.Register(action);
        return registry;
    }

    private static HighResolutionCaptureRequest Request(
        HighResolutionCaptureMode mode,
        string? actionId = null,
        SourceApplicationSnapshot? sourceSnapshot = null,
        Layers<ImageSpace>? layers = null) => new(
            new CaptureDecision(
                actionId ?? (mode == HighResolutionCaptureMode.Explicit
                    ? Toolbar.ToolbarCommandIds.HighResolution4K
                    : CaptureActionIds.Copy),
                new CaptureSelection
                {
                    Display = CaptureDecisionBinding.IdentityOf(FrozenDisplay()),
                    X = 400,
                    Y = 400,
                    Width = 600,
                    Height = 400,
                    Layers = layers ?? new Layers<ImageSpace>()
                },
                TargetWindow),
            [FrozenDisplay()],
            sourceSnapshot ?? SourceSnapshot(),
            mode);

    private static SourceApplicationSnapshot SourceSnapshot() => new(
        new SourceApplicationInfo(42, "Target", "target.exe", "Target window"),
        [new SourceWindowInfo(
            TargetWindow,
            42,
            "Target window",
            OriginalWindowBounds,
            TargetWindow,
            HierarchyDepth: 0)],
        TargetWindow);

    private static DisplaySnapshot FrozenDisplay() => new()
    {
        DisplayId = @"MONITOR\DISPLAY\0001",
        DeviceName = "Physical display",
        DisplayIndex = 0,
        IsPrimary = true,
        Width = 1920,
        Height = 1080,
        DpiScale = 1,
        Left = 0,
        Top = 0,
        PngData = [1]
    };

    private static VirtualWindowCaptureFrame Frame() => new(
        new DisplaySnapshot
        {
            DisplayId = @"MONITOR\MTT1337\0001",
            DeviceName = "VDD by MTT · 4K window",
            DisplayIndex = 0,
            IsPrimary = false,
            Width = 2400,
            Height = 1600,
            DpiScale = 2,
            Left = 1920,
            Top = 0,
            PngData = [1, 2, 3]
        },
        OriginalWindowBounds);

    private sealed class FakeFrameSource : IVirtualWindowFrameSource
    {
        private readonly VirtualWindowCaptureFrame _frame;
        private readonly Exception? _error;
        private readonly bool _waitForCancellation;

        public FakeFrameSource(
            VirtualWindowCaptureFrame frame,
            Exception? error = null,
            bool waitForCancellation = false)
        {
            _frame = frame;
            _error = error;
            _waitForCancellation = waitForCancellation;
        }

        public VirtualWindowCaptureTarget? LastTarget { get; private set; }

        public VirtualDisplayCaptureStatus GetStatus() => new(VirtualDisplayAvailability.Active);

        public async Task<VirtualWindowCaptureFrame> CaptureAsync(
            VirtualWindowCaptureTarget target,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            if (_waitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (_error is not null)
                throw _error;
            return _frame;
        }
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
        private readonly Exception? _error;

        public FakePersistence(Exception? error = null)
        {
            _error = error;
        }

        public int SaveCount { get; private set; }
        public CapturePersistenceRequest? LastRequest { get; private set; }

        public Task<StoredCapture> SaveAsync(
            CapturePersistenceRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastRequest = request;
            if (_error is not null)
                return Task.FromException<StoredCapture>(_error);
            return Task.FromResult(new StoredCapture(
                new ShotRecord(
                    1,
                    "sha",
                    request.CapturedAt,
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
                new RevisionRecord(1, 1, null, request.CapturedAt, null, "[]")));
        }
    }

    private sealed class RecordingAction : ICaptureAction
    {
        private readonly Exception? _error;

        public RecordingAction(string id, Exception? error = null)
        {
            _error = error;
            Descriptor = new CaptureActionDescriptor(
                id,
                id,
                id,
                new HashSet<CaptureActionScope> { CaptureActionScope.Capture });
        }

        public CaptureActionDescriptor Descriptor { get; }
        public int ExecutionCount { get; private set; }
        public CaptureContext? LastContext { get; private set; }

        public ValueTask PerformAsync(
            CaptureContext context,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            LastContext = context;
            if (_error is not null)
                return ValueTask.FromException(_error);
            return ValueTask.CompletedTask;
        }
    }
}
