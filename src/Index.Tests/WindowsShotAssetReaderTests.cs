using Index.Platform;
using Index.Storage;
using Microsoft.Data.Sqlite;

namespace Index.Tests;

public sealed class WindowsShotAssetReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-shot-assets-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadBestAvailablePrefersOriginal()
    {
        var store = await ShotStore.OpenAsync(_root);
        var shot = MakeShot("original");
        var original = new byte[] { 1, 2, 3 };
        var thumbnail = new byte[] { 4, 5 };
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
        var thumbnail = new byte[] { 7, 8, 9 };
        await File.WriteAllBytesAsync(store.ThumbnailPath(shot), thumbnail);

        var result = await new WindowsShotAssetReader(store).ReadBestAvailableAsync(shot);

        Assert.Equal(ShotAssetStatus.ThumbnailFallback, result.Status);
        Assert.Equal(thumbnail, result.Data.ToArray());
        Assert.NotNull(result.Warning);
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
}
