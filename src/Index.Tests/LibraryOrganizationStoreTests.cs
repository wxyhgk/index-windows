using Index.Annotation;
using Index.Storage;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace Index.Tests;

public sealed class LibraryOrganizationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-library-organization-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task FavoritesTagsAndCollectionsPersistAcrossReopen()
    {
        var shots = await ShotStore.OpenAsync(_root);
        var first = await SaveShotAsync(shots, SKColors.CornflowerBlue);
        var second = await SaveShotAsync(shots, SKColors.OrangeRed);
        var organization = await LibraryOrganizationStore.OpenAsync(_root);

        await organization.SetFavoriteAsync(first.Shot.Id, true);
        await organization.AddTagAsync(first.Shot.Id, "  论文  ");
        await organization.AddTagAsync(first.Shot.Id, "论文");
        var collection = await organization.CreateCollectionAsync("  MR-TADF  ", "发光分子");
        await organization.AddToCollectionAsync(collection.Id, first.Shot.Id);
        await organization.AddToCollectionAsync(collection.Id, first.Shot.Id);
        await organization.AddToCollectionAsync(collection.Id, second.Shot.Id);

        var reopened = await LibraryOrganizationStore.OpenAsync(_root);
        Assert.Contains(first.Shot.Id, await reopened.GetFavoriteIdsAsync());
        Assert.Equal(["论文"], await reopened.GetTagsAsync(first.Shot.Id));
        Assert.Equal([second.Shot.Id, first.Shot.Id], await reopened.GetCollectionShotIdsAsync(collection.Id));
        var summary = Assert.Single(await reopened.GetCollectionsAsync());
        Assert.Equal("MR-TADF", summary.Name);
        Assert.Equal("发光分子", summary.Note);
        Assert.Equal(2, summary.ItemCount);
    }

    [Fact]
    public async Task DeletingShotCascadesOrganizationWithoutDeletingCollection()
    {
        var shots = await ShotStore.OpenAsync(_root);
        var saved = await SaveShotAsync(shots, SKColors.MediumPurple);
        var organization = await LibraryOrganizationStore.OpenAsync(_root);
        await organization.SetFavoriteAsync(saved.Shot.Id, true);
        await organization.AddTagAsync(saved.Shot.Id, "工作");
        var collection = await organization.CreateCollectionAsync("工作集");
        await organization.AddToCollectionAsync(collection.Id, saved.Shot.Id);

        Assert.True(await shots.DeleteAsync(saved.Shot.Id));

        Assert.Empty(await organization.GetFavoriteIdsAsync());
        Assert.Empty(await organization.GetTagsAsync(saved.Shot.Id));
        Assert.Empty(await organization.GetCollectionShotIdsAsync(collection.Id));
        Assert.Equal(0, Assert.Single(await organization.GetCollectionsAsync()).ItemCount);
    }

    [Fact]
    public async Task CollectionNamesAreUniqueIgnoringCaseAndWhitespace()
    {
        var organization = await LibraryOrganizationStore.OpenAsync(_root);
        await organization.CreateCollectionAsync("Research");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => organization.CreateCollectionAsync("  research  "));
    }

    private static async Task<StoredCapture> SaveShotAsync(ShotStore store, SKColor color)
    {
        using var bitmap = new SKBitmap(12, 8);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return await store.SaveCaptureAsync(
            data.ToArray(),
            new ShotCaptureMetadata
            {
                RegionX = 0,
                RegionY = 0,
                RegionWidth = 12,
                RegionHeight = 8
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
