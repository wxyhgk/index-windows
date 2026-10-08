using Index.Annotation;
using Index.Editor;
using Index.Storage;
using SkiaSharp;

namespace Index.Tests;

public sealed class ShotEditorSessionControllerTests
{
    [Fact]
    public async Task LoadRestoresLatestCompleteRevisionWithoutCreatingHistory()
    {
        var repository = new FakeEditorRepository
        {
            History = [Snapshot(9, null, Layers(RectLayer()), "initial")]
        };
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository);

        var result = await controller.LoadAsync();

        Assert.True(result.CanEdit);
        var layer = Assert.Single(controller.Annotation.Layers.Elements);
        Assert.Equal(LayerKind.Rect, layer.Kind);
        Assert.False(controller.Annotation.CanUndo);
        Assert.False(controller.HasPendingChanges);
        Assert.Empty(repository.Appended);
    }

    [Fact]
    public async Task FlushAppendsOnlyChangedCompleteState()
    {
        var repository = new FakeEditorRepository
        {
            History = [Snapshot(1, null, Layers(RectLayer()), "initial")]
        };
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));
        await controller.LoadAsync();

        controller.Annotation.Tool = AnnotationTool.Ellipse;
        controller.Annotation.BeginDraw(new PointF(30, 20));
        controller.Annotation.UpdateDraw(new PointF(60, 45));
        controller.Annotation.EndDraw();

        Assert.True(controller.HasPendingChanges);
        Assert.True(await controller.FlushAsync());
        Assert.False(await controller.FlushAsync());
        Assert.False(controller.HasPendingChanges);
        var saved = Assert.Single(repository.Appended);
        Assert.Equal(2, saved.Count);
    }

    [Fact]
    public async Task AutoSaveDebouncesAnnotationChanges()
    {
        var repository = new FakeEditorRepository();
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMilliseconds(15));
        await controller.LoadAsync();

        controller.Annotation.Tool = AnnotationTool.Rect;
        controller.Annotation.BeginDraw(new PointF(3, 4));
        controller.Annotation.UpdateDraw(new PointF(20, 14));
        controller.Annotation.EndDraw();

        await repository.FirstAppend.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(40);
        Assert.Single(repository.Appended);
        Assert.False(controller.HasPendingChanges);
    }

    [Fact]
    public async Task DisposePersistsPendingSnapshotWithoutDuplicatingLatestContent()
    {
        var repository = new FakeEditorRepository();
        var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));
        await controller.LoadAsync();
        controller.Annotation.Tool = AnnotationTool.Rect;
        controller.Annotation.BeginDraw(new PointF(3, 4));
        controller.Annotation.UpdateDraw(new PointF(20, 14));
        controller.Annotation.EndDraw();

        await controller.DisposeAsync();

        Assert.Single(repository.Appended);
        Assert.Single(repository.Appended[0].Elements);
    }

    [Fact]
    public async Task ThumbnailFallbackIsReadOnlyAndNeverPersists()
    {
        var repository = new FakeEditorRepository();
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.ThumbnailFallback, Png()),
            repository,
            TimeSpan.Zero);

        var result = await controller.LoadAsync();
        controller.Annotation.Tool = AnnotationTool.Rect;
        controller.Annotation.BeginDraw(new PointF(1, 1));
        controller.Annotation.UpdateDraw(new PointF(12, 12));
        controller.Annotation.EndDraw();
        await Task.Delay(20);

        Assert.Equal(ShotEditorLoadStatus.Degraded, result.Status);
        Assert.False(result.CanEdit);
        Assert.False(await controller.FlushAsync());
        Assert.Empty(repository.Appended);
    }

    [Fact]
    public async Task LoadAndAutoSaveUseConfiguredSessionDispatcher()
    {
        var repository = new FakeEditorRepository();
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMilliseconds(15));
        int dispatchCount = 0;
        controller.SetSessionDispatcher(async (operation, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref dispatchCount);
            await operation();
        });

        await controller.LoadAsync();
        controller.Annotation.Tool = AnnotationTool.Rect;
        controller.Annotation.BeginDraw(new PointF(3, 4));
        controller.Annotation.UpdateDraw(new PointF(20, 14));
        controller.Annotation.EndDraw();
        await repository.FirstAppend.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(Volatile.Read(ref dispatchCount) >= 2);
    }

    [Fact]
    public async Task LoadExposesCompleteImmutableHistoryAndSelectsLatestRevision()
    {
        var repository = new FakeEditorRepository
        {
            History =
            [
                Snapshot(4, null, Layers(), "original"),
                Snapshot(7, 4, Layers(RectLayer()), "rectangle")
            ]
        };
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));

        await controller.LoadAsync();

        Assert.Equal(7, controller.CurrentRevisionId);
        Assert.Collection(
            controller.RevisionHistory,
            item =>
            {
                Assert.Equal(4, item.RevisionId);
                Assert.Null(item.ParentRevisionId);
                Assert.Equal("original", item.Note);
                Assert.Equal(0, item.LayerCount);
            },
            item =>
            {
                Assert.Equal(7, item.RevisionId);
                Assert.Equal(4, item.ParentRevisionId);
                Assert.Equal("rectangle", item.Note);
                Assert.Equal(1, item.LayerCount);
            });
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ShotEditorHistoryItem>)controller.RevisionHistory).Add(
                new ShotEditorHistoryItem(8, 7, DateTimeOffset.UtcNow, null, 0)));
    }

    [Fact]
    public async Task SelectingHistoryRestoresCompleteStateAndResetsUndoWithoutSaving()
    {
        var firstLayer = RectLayer();
        var repository = new FakeEditorRepository
        {
            History =
            [
                Snapshot(1, null, Layers(firstLayer), "first"),
                Snapshot(2, 1, Layers(firstLayer, EllipseLayer()), "second")
            ]
        };
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));
        await controller.LoadAsync();
        controller.Annotation.Tool = AnnotationTool.Rect;
        controller.Annotation.BeginDraw(new PointF(5, 5));
        controller.Annotation.UpdateDraw(new PointF(20, 20));
        controller.Annotation.EndDraw();
        Assert.True(controller.Annotation.CanUndo);
        Assert.True(controller.HasPendingChanges);

        Assert.True(await controller.SelectRevisionAsync(1));

        Assert.Equal(1, controller.CurrentRevisionId);
        Assert.Single(controller.Annotation.Layers.Elements);
        Assert.False(controller.Annotation.CanUndo);
        Assert.False(controller.Annotation.CanRedo);
        Assert.False(controller.HasPendingChanges);
        Assert.Empty(repository.Appended);
    }

    [Fact]
    public async Task EditingHistoricalRevisionAppendsToDatabaseTailAndRetainsHistory()
    {
        var repository = new FakeEditorRepository
        {
            History =
            [
                Snapshot(1, null, Layers(RectLayer()), "first"),
                Snapshot(2, 1, Layers(RectLayer(), EllipseLayer()), "second")
            ]
        };
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));
        await controller.LoadAsync();
        await controller.SelectRevisionAsync(1);
        controller.Annotation.Tool = AnnotationTool.Line;
        controller.Annotation.BeginDraw(new PointF(2, 2));
        controller.Annotation.UpdateDraw(new PointF(30, 20));
        controller.Annotation.EndDraw();

        Assert.True(await controller.FlushAsync());

        var appended = Assert.Single(repository.AppendedRecords);
        Assert.Equal(2, appended.ParentId);
        Assert.Equal(appended.Id, controller.CurrentRevisionId);
        Assert.Equal(3, controller.RevisionHistory.Count);
        Assert.Equal([1L, 2L, appended.Id],
            controller.RevisionHistory.Select(item => item.RevisionId));
        Assert.Equal(2, controller.Annotation.Layers.Count);
        Assert.False(controller.HasPendingChanges);
    }

    [Fact]
    public async Task LatestConcurrentHistorySelectionWinsGenerationAndObserverErrorsAreIsolated()
    {
        var repository = new FakeEditorRepository
        {
            History =
            [
                Snapshot(1, null, Layers(RectLayer()), "first"),
                Snapshot(2, 1, Layers(RectLayer(), EllipseLayer()), "second")
            ]
        };
        var holdDispatch = false;
        var dispatchEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));
        controller.SetSessionDispatcher(async (operation, cancellationToken) =>
        {
            if (holdDispatch)
            {
                dispatchEntered.TrySetResult();
                await releaseDispatch.Task.WaitAsync(cancellationToken);
            }
            await operation();
        });
        await controller.LoadAsync();
        await controller.SelectRevisionAsync(1);
        controller.StateChanged += () => throw new InvalidOperationException("observer");
        holdDispatch = true;

        var staleSelection = controller.SelectRevisionAsync(1);
        await dispatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var winningSelection = controller.SelectRevisionAsync(2);
        releaseDispatch.TrySetResult();

        Assert.False(await staleSelection);
        Assert.True(await winningSelection);
        Assert.Equal(2, controller.CurrentRevisionId);
        Assert.Equal(2, controller.Annotation.Layers.Count);
    }

    [Fact]
    public async Task CancelledHistorySelectionLeavesCurrentRevisionUntouched()
    {
        var repository = new FakeEditorRepository
        {
            History =
            [
                Snapshot(1, null, Layers(RectLayer()), "first"),
                Snapshot(2, 1, Layers(RectLayer(), EllipseLayer()), "second")
            ]
        };
        var holdDispatch = false;
        var dispatchEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var neverRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new ShotEditorSessionController(
            Shot(),
            new FakeAssetReader(ShotAssetStatus.Original, Png()),
            repository,
            TimeSpan.FromMinutes(1));
        controller.SetSessionDispatcher(async (operation, cancellationToken) =>
        {
            if (holdDispatch)
            {
                dispatchEntered.TrySetResult();
                await neverRelease.Task.WaitAsync(cancellationToken);
            }
            await operation();
        });
        await controller.LoadAsync();
        holdDispatch = true;
        using var cancellation = new CancellationTokenSource();

        var selection = controller.SelectRevisionAsync(1, cancellation.Token);
        await dispatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => selection);
        Assert.Equal(2, controller.CurrentRevisionId);
        Assert.Equal(2, controller.Annotation.Layers.Count);
        Assert.False(controller.HasPendingChanges);
    }

    private static ShotRecord Shot() => new(
        42,
        new string('a', 64),
        DateTimeOffset.UtcNow,
        80,
        50,
        1,
        "Test",
        "test.exe",
        "Editor test",
        null,
        0,
        "Display",
        0,
        0,
        80,
        50,
        "png");

    private static Layer RectLayer() => new(
        LayerKind.Rect,
        new LRect(2, 3, 12, 10),
        new LColor(1, 0, 0, 1),
        2);

    private static Layer EllipseLayer() => new(
        LayerKind.Ellipse,
        new LRect(20, 10, 15, 12),
        new LColor(0, 0, 1, 1),
        3);

    private static ShotRevisionSnapshot Snapshot(
        long id,
        long? parentId,
        Layers<ImageSpace> layers,
        string? note) => new(id, layers)
        {
            ParentRevisionId = parentId,
            CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(id),
            Note = note
        };

    private static Layers<ImageSpace> Layers(params Layer[] layers)
    {
        var result = new Layers<ImageSpace>();
        foreach (var layer in layers)
            result.Append(layer);
        return result;
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(80, 50);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class FakeAssetReader(
        ShotAssetStatus status,
        ReadOnlyMemory<byte> data) : IShotAssetReader
    {
        public Task<ShotAssetReadResult> ReadBestAvailableAsync(
            ShotRecord shot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ShotAssetReadResult(status, data, "fallback"));
    }

    private sealed class FakeEditorRepository : IShotEditorRepository
    {
        private long _nextRevisionId = 100;

        public List<ShotRevisionSnapshot> History { get; init; } = [];
        public List<Layers<ImageSpace>> Appended { get; } = [];
        public List<RevisionRecord> AppendedRecords { get; } = [];
        public TaskCompletionSource FirstAppend { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ShotRevisionSnapshot?> GetLatestRevisionSnapshotAsync(
            long shotId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(History.LastOrDefault());

        public Task<IReadOnlyList<ShotRevisionSnapshot>> GetRevisionHistoryAsync(
            long shotId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ShotRevisionSnapshot>>(History.ToArray());

        public Task<RevisionRecord> AppendRevisionAsync(
            long shotId,
            Layers<ImageSpace> layers,
            string? note = null,
            CancellationToken cancellationToken = default)
        {
            var copy = Layers(layers.Elements.Select(layer => layer with { }).ToArray());
            Appended.Add(copy);
            FirstAppend.TrySetResult();
            var record = new RevisionRecord(
                Interlocked.Increment(ref _nextRevisionId),
                shotId,
                History.LastOrDefault()?.RevisionId,
                DateTimeOffset.UtcNow,
                note,
                "[]");
            History.Add(new ShotRevisionSnapshot(record.Id, copy)
            {
                ParentRevisionId = record.ParentId,
                CreatedAt = record.CreatedAt,
                Note = record.Note
            });
            AppendedRecords.Add(record);
            return Task.FromResult(record);
        }
    }
}
