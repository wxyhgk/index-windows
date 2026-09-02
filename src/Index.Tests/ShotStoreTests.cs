using Index.Annotation;
using Index.Storage;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace Index.Tests;

public sealed class ShotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-storage-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveCaptureCreatesContentAddressedFilesAndRevisionChain()
    {
        var store = await ShotStore.OpenAsync(_root);
        var png = MakePng(80, 50);
        var layers = MakeLayers();
        var capturedAt = new DateTimeOffset(2026, 8, 23, 9, 30, 0, TimeSpan.Zero);

        var result = await store.SaveCaptureAsync(
            png,
            new ShotCaptureMetadata
            {
                CapturedAt = capturedAt,
                Scale = 1.5,
                DisplayIndex = 2,
                RegionX = 10,
                RegionY = 20,
                RegionWidth = 80,
                RegionHeight = 50
            },
            layers);

        Assert.Equal(80, result.Shot.PixelWidth);
        Assert.Equal(50, result.Shot.PixelHeight);
        Assert.Equal(64, result.Shot.Sha256.Length);
        Assert.True(File.Exists(Path.Combine(_root, "originals", $"{result.Shot.Sha256}.png")));
        var thumbnailPath = Path.Combine(_root, "thumbnails", $"{result.Shot.Sha256}.png");
        Assert.True(File.Exists(thumbnailPath));
        var thumbnailHeader = (await File.ReadAllBytesAsync(thumbnailPath)).Take(8).ToArray();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, thumbnailHeader);

        var revisions = await store.GetRevisionsAsync(result.Shot.Id);
        Assert.Equal(2, revisions.Count);
        Assert.Null(revisions[0].ParentId);
        Assert.Equal(revisions[0].Id, revisions[1].ParentId);
        Assert.Equal("原始", revisions[0].Note);
        Assert.Equal("截图标注", revisions[1].Note);
        Assert.Contains("\"kind\":\"rect\"", revisions[1].LayersJson);
        Assert.Contains("\"lineWidth\":3", revisions[1].LayersJson);
        Assert.DoesNotContain("handleBounds", revisions[1].LayersJson);
        Assert.DoesNotContain("minX", revisions[1].LayersJson);
    }

    [Fact]
    public async Task SaveCapture_NotifiesObserversAfterCommittedShotCanBeRead()
    {
        var store = await ShotStore.OpenAsync(_root);
        StoredCapture? notification = null;
        store.CaptureSaved += capture => notification = capture;

        var saved = await store.SaveCaptureAsync(
            MakePng(20, 14), Metadata(20, 14), new Layers<ImageSpace>());

        Assert.NotNull(notification);
        Assert.Equal(saved.Shot.Id, notification!.Shot.Id);
        Assert.Equal(saved.Shot.Id, Assert.Single(await store.GetRecentAsync()).Id);
    }

    [Fact]
    public async Task AppendRevisionLinksToCurrentTail()
    {
        var store = await ShotStore.OpenAsync(_root);
        var saved = await store.SaveCaptureAsync(
            MakePng(20, 20), Metadata(20, 20), new Layers<ImageSpace>());

        var appended = await store.AppendRevisionAsync(saved.Shot.Id, MakeLayers(), "编辑");
        var revisions = await store.GetRevisionsAsync(saved.Shot.Id);

        Assert.Equal(2, revisions.Count);
        Assert.Equal(revisions[0].Id, appended.ParentId);
        Assert.Equal("编辑", appended.Note);
    }

    [Fact]
    public async Task DeleteKeepsSharedOriginalUntilLastReferenceIsGone()
    {
        var store = await ShotStore.OpenAsync(_root);
        var png = MakePng(16, 12);
        var first = await store.SaveCaptureAsync(png, Metadata(16, 12), new Layers<ImageSpace>());
        var second = await store.SaveCaptureAsync(png, Metadata(16, 12), new Layers<ImageSpace>());
        var original = Path.Combine(_root, "originals", $"{first.Shot.Sha256}.png");

        Assert.Equal(first.Shot.Sha256, second.Shot.Sha256);
        Assert.True(await store.DeleteAsync(first.Shot.Id));
        Assert.True(File.Exists(original));
        Assert.True(await store.DeleteAsync(second.Shot.Id));
        Assert.False(File.Exists(original));
        Assert.Empty(await store.GetRecentAsync());
    }

    [Fact]
    public async Task ReopenRunsMigrationIdempotentlyAndPreservesRows()
    {
        var firstStore = await ShotStore.OpenAsync(_root);
        var saved = await firstStore.SaveCaptureAsync(
            MakePng(24, 18), Metadata(24, 18), new Layers<ImageSpace>());

        var reopened = await ShotStore.OpenAsync(_root);
        var rows = await reopened.GetRecentAsync();

        var row = Assert.Single(rows);
        Assert.Equal(saved.Shot.Id, row.Id);
        Assert.Equal(saved.Shot.Sha256, row.Sha256);
    }

    [Fact]
    public async Task GetByIdsIgnoresMissingRowsAndReturnsNewestFirst()
    {
        var store = await ShotStore.OpenAsync(_root);
        var older = await store.SaveCaptureAsync(
            MakePng(18, 12),
            Metadata(18, 12) with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            new Layers<ImageSpace>());
        var newer = await store.SaveCaptureAsync(
            MakePng(19, 13),
            Metadata(19, 13) with { CapturedAt = DateTimeOffset.UtcNow },
            new Layers<ImageSpace>());

        var rows = await store.GetByIdsAsync([older.Shot.Id, 999_999, newer.Shot.Id]);

        Assert.Equal([newer.Shot.Id, older.Shot.Id], rows.Select(row => row.Id));
    }

    [Fact]
    public async Task CursorPagesCoverAllRowsWithoutDuplicates()
    {
        var store = await ShotStore.OpenAsync(_root);
        var capturedAt = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 7; index++)
        {
            await store.SaveCaptureAsync(
                MakePng(16 + index, 12),
                Metadata(16 + index, 12) with { CapturedAt = capturedAt.AddSeconds(index / 2) },
                new Layers<ImageSpace>());
        }

        var ids = new List<long>();
        ShotPageCursor? cursor = null;
        do
        {
            var page = await store.GetPageAsync(3, cursor);
            ids.AddRange(page.Items.Select(shot => shot.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(7, await store.GetCountAsync());
        Assert.Equal(7, ids.Count);
        Assert.Equal(7, ids.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(id => id), ids);
    }

    [Fact]
    public async Task CapturedApplicationsAggregateStableIdentityAndRecentPreviews()
    {
        var store = await ShotStore.OpenAsync(_root);
        var baseline = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        var edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        await store.SaveCaptureAsync(
            MakePng(20, 10),
            Metadata(20, 10) with
            {
                CapturedAt = baseline,
                AppName = "Microsoft Edge",
                AppIdentifier = edgePath,
                WindowTitle = "旧页面"
            },
            new Layers<ImageSpace>());
        var newestEdge = await store.SaveCaptureAsync(
            MakePng(21, 11),
            Metadata(21, 11) with
            {
                CapturedAt = baseline.AddMinutes(2),
                AppName = "Microsoft Edge",
                AppIdentifier = edgePath.ToUpperInvariant(),
                WindowTitle = "新页面"
            },
            new Layers<ImageSpace>());
        await store.SaveCaptureAsync(
            MakePng(24, 14),
            Metadata(24, 14) with
            {
                CapturedAt = baseline.AddMinutes(1),
                AppName = "Microsoft Edge",
                AppIdentifier = @"D:\Portable\Edge\msedge.exe",
                WindowTitle = "便携版页面"
            },
            new Layers<ImageSpace>());
        await store.SaveCaptureAsync(
            MakePng(22, 12),
            Metadata(22, 12) with
            {
                CapturedAt = baseline.AddMinutes(1),
                AppName = "文件资源管理器",
                AppIdentifier = @"C:\Windows\explorer.exe"
            },
            new Layers<ImageSpace>());
        await store.SaveCaptureAsync(
            MakePng(23, 13),
            Metadata(23, 13) with { CapturedAt = baseline.AddMinutes(3) },
            new Layers<ImageSpace>());

        var applications = await store.GetCapturedApplicationsAsync(previewLimit: 1);

        Assert.Equal(2, applications.Count);
        var edge = applications.Single(application => application.Name == "Microsoft Edge");
        Assert.Equal(3, edge.CaptureCount);
        Assert.Equal(baseline.AddMinutes(2), edge.LastCapturedAt);
        Assert.Equal(newestEdge.Shot.Id, Assert.Single(edge.Previews).Id);
    }

    [Fact]
    public async Task ApplicationPagesOnlyReturnSelectedApplicationAndPreserveCursorOrder()
    {
        var store = await ShotStore.OpenAsync(_root);
        var baseline = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 5; index++)
        {
            await store.SaveCaptureAsync(
                MakePng(30 + index, 20),
                Metadata(30 + index, 20) with
                {
                    CapturedAt = baseline.AddSeconds(index),
                    AppName = index == 4 ? "记事本" : "Microsoft Edge",
                    AppIdentifier = index == 4 ? "notepad.exe" : "msedge.exe"
                },
                new Layers<ImageSpace>());
        }

        var identity = new CapturedApplicationIdentity("Microsoft Edge", "MSEDGE.EXE");
        var ids = new List<long>();
        ShotPageCursor? cursor = null;
        do
        {
            var page = await store.GetApplicationPageAsync(identity, 2, cursor);
            ids.AddRange(page.Items.Select(shot => shot.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(4, ids.Count);
        Assert.Equal(4, ids.Distinct().Count());
        Assert.All(await store.GetByIdsAsync(ids), shot => Assert.Equal("Microsoft Edge", shot.AppName));
        Assert.Equal(ids.OrderByDescending(id => id), ids);
    }

    private static ShotCaptureMetadata Metadata(int width, int height) => new()
    {
        RegionX = 0,
        RegionY = 0,
        RegionWidth = width,
        RegionHeight = height
    };

    private static Layers<ImageSpace> MakeLayers()
    {
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Rect,
            new LRect(2, 3, 30, 20),
            new LColor(1, 0, 0, 1),
            3));
        return layers;
    }

    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite 默认连接池会继续持有数据库文件；测试结束显式清池。
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
