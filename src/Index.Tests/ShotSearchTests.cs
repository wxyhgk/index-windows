using Index.Annotation;
using Index.Storage;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace Index.Tests;

public sealed class ShotSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-shot-search-{Guid.NewGuid():N}");

    [Fact]
    public async Task SearchCoversMetadataAndAttributesIncludingShortChineseQueries()
    {
        var shots = await ShotStore.OpenAsync(_root);
        var organization = await LibraryOrganizationStore.OpenAsync(_root);
        var chinese = await SaveAsync(shots, "微信", "项目讨论", null);
        var terminal = await SaveAsync(shots, "Windows Terminal", "build output", "https://example.test/build");
        await organization.AddTagAsync(terminal.Shot.Id, "发布流程");

        Assert.Equal(chinese.Shot.Id, Assert.Single(await shots.SearchAsync("微信")).Id);
        Assert.Equal(terminal.Shot.Id, Assert.Single(await shots.SearchAsync("Terminal")).Id);
        Assert.Equal(terminal.Shot.Id, Assert.Single(await shots.SearchAsync("发布流程")).Id);
        Assert.Equal(terminal.Shot.Id, Assert.Single(await shots.SearchAsync("example.test")).Id);
    }

    [Fact]
    public async Task SearchTreatsLikeWildcardsAsLiteralCharacters()
    {
        var shots = await ShotStore.OpenAsync(_root);
        var literal = await SaveAsync(shots, "100%", "literal", null);
        await SaveAsync(shots, "ordinary", "literal", null);

        Assert.Equal(literal.Shot.Id, Assert.Single(await shots.SearchAsync("%")).Id);
    }

    private static async Task<StoredCapture> SaveAsync(
        ShotStore store,
        string appName,
        string windowTitle,
        string? sourceUrl)
    {
        using var bitmap = new SKBitmap(24, 18);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return await store.SaveCaptureAsync(
            data.ToArray(),
            new ShotCaptureMetadata
            {
                AppName = appName,
                WindowTitle = windowTitle,
                SourceUrl = sourceUrl,
                RegionX = 0,
                RegionY = 0,
                RegionWidth = 24,
                RegionHeight = 18
            },
            new Layers<ImageSpace>());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
