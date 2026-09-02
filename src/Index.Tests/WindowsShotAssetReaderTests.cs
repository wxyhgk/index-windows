using Index.Platform;
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

    private static byte[] MakePng(SKColor color)
        => Encode(color, SKEncodedImageFormat.Png);

    private static byte[] MakeJpeg(SKColor color)
        => Encode(color, SKEncodedImageFormat.Jpeg);

    private static byte[] Encode(SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }
}
