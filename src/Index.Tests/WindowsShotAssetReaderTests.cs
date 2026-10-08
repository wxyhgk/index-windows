using Index.Platform;
using Index.Annotation;
using Index.Storage;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using System.Runtime.Versioning;

namespace Index.Tests;

[SupportedOSPlatform("windows6.1")]
public sealed class WindowsShotAssetReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-shot-assets-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadBestAvailablePrefersOriginal()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("original");
        var original = MakePng(SKColors.Red);
        var thumbnail = MakePng(SKColors.Blue);
        await File.WriteAllBytesAsync(store.OriginalPath(shot), original);
        await File.WriteAllBytesAsync(store.ThumbnailPath(shot), thumbnail);

        var result = await new WindowsShotAssetReader(store).ReadBestAvailableAsync(shot);

        Assert.Equal(ShotAssetStatus.Original, result.Status);
        Assert.Equal(original, result.Data.ToArray());
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task ReadBestAvailableFallsBackToThumbnailWithWarning()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("fallback");
        var thumbnail = MakePng(SKColors.Green);
        await File.WriteAllBytesAsync(store.ThumbnailPath(shot), thumbnail);

        var result = await new WindowsShotAssetReader(store).ReadBestAvailableAsync(shot);

        Assert.Equal(ShotAssetStatus.ThumbnailFallback, result.Status);
        Assert.Equal(thumbnail, result.Data.ToArray());
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task ReadBestAvailableFallsBackToLegacyJpegDuringMigration()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("legacy-fallback");
        var thumbnail = MakeJpeg(SKColors.Yellow);
        await File.WriteAllBytesAsync(store.LegacyThumbnailPath(shot), thumbnail);

        var result = await new WindowsShotAssetReader(store).ReadBestAvailableAsync(shot);

        Assert.Equal(ShotAssetStatus.ThumbnailFallback, result.Status);
        Assert.Equal("image/png", result.MediaType);
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], result.Data.Span[..8].ToArray());
        Assert.NotEqual(thumbnail, result.Data.ToArray());
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task ReadBestAvailableFallsBackWhenOriginalIsCorrupt()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("corrupt-original");
        var thumbnail = MakePng(SKColors.Purple);
        await File.WriteAllBytesAsync(store.OriginalPath(shot), [1, 2, 3]);
        await File.WriteAllBytesAsync(store.ThumbnailPath(shot), thumbnail);

        var result = await new WindowsShotAssetReader(store).ReadBestAvailableAsync(shot);

        Assert.Equal(ShotAssetStatus.ThumbnailFallback, result.Status);
        Assert.Equal(thumbnail, result.Data.ToArray());
        Assert.Contains("损坏", result.Warning);
    }

    [Fact]
    public async Task ReadBestAvailableReportsCorruptWhenNoValidAssetExists()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("corrupt-only");
        await File.WriteAllBytesAsync(store.OriginalPath(shot), [1, 2, 3]);

        var result = await new WindowsShotAssetReader(store).ReadBestAvailableAsync(shot);

        Assert.Equal(ShotAssetStatus.Corrupt, result.Status);
        Assert.False(result.HasData);
        Assert.Contains("损坏", result.Warning);
    }

    [Fact]
    public async Task ReadPreviewFallsBackToOriginalWhenThumbnailIsCorrupt()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("corrupt-thumbnail");
        var original = MakePng(SKColors.Orange);
        await File.WriteAllBytesAsync(store.OriginalPath(shot), original);
        await File.WriteAllBytesAsync(store.ThumbnailPath(shot), [1, 2, 3]);

        var result = await new WindowsShotAssetReader(store).ReadPreviewAsync(shot);

        Assert.Equal(ShotAssetStatus.Original, result.Status);
        Assert.Equal(original, result.Data.ToArray());
        Assert.Contains("损坏", result.Warning);
    }

    [Fact]
    public async Task ReadBestAvailableReportsMissingWithoutThrowing()
    {
        var store = await ShotStore.OpenAsync(_root);

        var result = await new WindowsShotAssetReader(store)
            .ReadBestAvailableAsync(MakeShot("missing"));

        Assert.Equal(ShotAssetStatus.Missing, result.Status);
        Assert.False(result.HasData);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task RenderedAndPreviewAssetsApplyLatestAnnotationRevision()
    {
        var store = await ShotStore.OpenAsync(_root);
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Rect,
            new LRect(4, 4, 28, 18),
            new LColor(1, 0, 0, 1),
            4));
        var saved = await store.SaveCaptureAsync(
            MakePng(SKColors.CornflowerBlue, 48, 32),
            new ShotCaptureMetadata
            {
                RegionX = 0,
                RegionY = 0,
                RegionWidth = 48,
                RegionHeight = 32
            },
            layers);
        var reader = new WindowsShotAssetReader(store);

        var original = await reader.ReadBestAvailableAsync(saved.Shot);
        var rendered = await reader.ReadRenderedAsync(saved.Shot);
        var preview = await reader.ReadPreviewAsync(saved.Shot);

        Assert.NotEqual(original.Data.ToArray(), rendered.Data.ToArray());
        AssertAnnotated(rendered.Data);
        AssertAnnotated(preview.Data);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ShotRecord MakeShot(string sha256) => new(
        Id: 1,
        Sha256: sha256,
        CapturedAt: DateTimeOffset.UtcNow,
        PixelWidth: 10,
        PixelHeight: 10,
        Scale: 1,
        AppName: null,
        AppIdentifier: null,
        WindowTitle: null,
        SourceUrl: null,
        DisplayIndex: null,
        DisplayName: null,
        RegionX: 0,
        RegionY: 0,
        RegionWidth: 10,
        RegionHeight: 10,
        OriginalExtension: "png");

    private static byte[] MakePng(SKColor color, int width = 2, int height = 2)
        => Encode(color, SKEncodedImageFormat.Png, width, height);

    private static byte[] MakeJpeg(SKColor color)
        => Encode(color, SKEncodedImageFormat.Jpeg, 2, 2);

    private static byte[] Encode(
        SKColor color,
        SKEncodedImageFormat format,
        int width,
        int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    private static void AssertAnnotated(ReadOnlyMemory<byte> png)
    {
        using var bitmap = SKBitmap.Decode(png.ToArray());
        Assert.NotNull(bitmap);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Red > 200 && pixel.Blue < 80);
    }
}
