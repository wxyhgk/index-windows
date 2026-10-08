using Index.Gallery;
using Index.Platform;
using Index.Platform.Clipboard;
using Index.Platform.Export;
using Index.Storage;

namespace Index.Tests;

public sealed class GalleryShotCommandServiceTests
{
    [Fact]
    public async Task CopyAndExportUseResolvedPngBytes()
    {
        var dependencies = new Dependencies(new ShotAssetReadResult(
            ShotAssetStatus.ThumbnailFallback,
            new byte[] { 1, 2, 3 },
            "original missing"));
        var service = dependencies.CreateService();
        var shot = Shot();

        await service.CopyAsync(shot);
        var path = await service.ExportAsync(shot);

        Assert.Equal(new byte[] { 1, 2, 3 }, dependencies.Clipboard.Png);
        Assert.Equal(new byte[] { 1, 2, 3 }, dependencies.Exporter.Png);
        Assert.StartsWith("Index_", dependencies.Exporter.SuggestedName);
        Assert.Equal("export.png", path);
    }

    [Theory]
    [InlineData(ShotAssetStatus.Missing, false)]
    [InlineData(ShotAssetStatus.Corrupt, true)]
    public async Task UnreadableAssetReturnsSpecificFailure(
        ShotAssetStatus status,
        bool isCorrupt)
    {
        var service = new Dependencies(new ShotAssetReadResult(
            status,
            ReadOnlyMemory<byte>.Empty,
            "unreadable")).CreateService();

        var error = await Record.ExceptionAsync(() => service.CopyAsync(Shot()));

        Assert.IsType(isCorrupt ? typeof(InvalidDataException) : typeof(FileNotFoundException), error);
    }

    [Fact]
    public void OpenSourceOnlyDelegatesHttpAndHttpsUris()
    {
        var dependencies = new Dependencies();
        var service = dependencies.CreateService();

        Assert.False(service.OpenSource(Shot("file:///C:/secret.png")));
        Assert.False(service.OpenSource(Shot("not a uri")));
        Assert.True(service.OpenSource(Shot("https://example.test/source")));

        Assert.Equal(
            new Uri("https://example.test/source"),
            Assert.Single(dependencies.UriOpener.Opened));
    }

    [Fact]
    public async Task StorageCommandsUseNarrowRepositories()
    {
        var dependencies = new Dependencies();
        dependencies.Repository.IsFavorite = true;
        dependencies.Repository.Tags = ["chemistry", "paper"];
        var service = dependencies.CreateService();
        var shot = Shot();

        Assert.True(await service.IsFavoriteAsync(shot));
        await service.SetFavoriteAsync(shot, false);
        Assert.Equal(["chemistry", "paper"], await service.GetTagsAsync(shot));
        Assert.True(await service.DeleteAsync(shot));
        await service.OpenOriginalAsync(shot);

        Assert.Equal(shot.Id, dependencies.Repository.LastShotId);
        Assert.False(dependencies.Repository.LastFavorite);
        Assert.Equal(shot, dependencies.AssetOpener.Opened);
    }

    [Fact]
    public async Task CopyOriginalPathUsesValidatedPathResolverAndTextClipboard()
    {
        var dependencies = new Dependencies();
        var service = dependencies.CreateService();

        string path = await service.CopyOriginalPathAsync(Shot());

        Assert.Equal(@"C:\Index\originals\shot.png", path);
        Assert.Equal(path, dependencies.Clipboard.Text);
        Assert.Equal(Shot().Id, dependencies.AssetOpener.Resolved?.Id);
    }

    private static ShotRecord Shot(string? sourceUrl = null) => new(
        42,
        "sha",
        DateTimeOffset.UtcNow,
        100,
        80,
        1,
        null,
        null,
        null,
        sourceUrl,
        0,
        "display",
        0,
        0,
        100,
        80,
        "png");

    private sealed class Dependencies
    {
        public Dependencies(ShotAssetReadResult? asset = null)
        {
            Assets = new FakeAssets(asset ?? new ShotAssetReadResult(
                ShotAssetStatus.Original,
                new byte[] { 9 }));
        }

        public FakeRepository Repository { get; } = new();
        public FakeAssets Assets { get; }
        public FakeAssetOpener AssetOpener { get; } = new();
        public FakeUriOpener UriOpener { get; } = new();
        public FakeClipboard Clipboard { get; } = new();
        public FakeExporter Exporter { get; } = new();

        public GalleryShotCommandService CreateService() => new(
            Repository,
            Repository,
            Assets,
            AssetOpener,
            AssetOpener,
            UriOpener,
            Clipboard,
            Exporter);
    }

    private sealed class FakeRepository
        : IShotDeletionRepository, IShotOrganizationRepository
    {
        public bool IsFavorite { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = [];
        public long LastShotId { get; private set; }
        public bool? LastFavorite { get; private set; }

        public Task<bool> DeleteAsync(long shotId, CancellationToken cancellationToken = default)
        {
            LastShotId = shotId;
            return Task.FromResult(true);
        }

        public Task<bool> IsFavoriteAsync(
            long shotId,
            CancellationToken cancellationToken = default)
        {
            LastShotId = shotId;
            return Task.FromResult(IsFavorite);
        }

        public Task SetFavoriteAsync(
            long shotId,
            bool isFavorite,
            CancellationToken cancellationToken = default)
        {
            LastShotId = shotId;
            LastFavorite = isFavorite;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetTagsAsync(
            long shotId,
            CancellationToken cancellationToken = default)
        {
            LastShotId = shotId;
            return Task.FromResult(Tags);
        }
    }

    private sealed class FakeAssets(ShotAssetReadResult result) : IShotAssetReader
    {
        public Task<ShotAssetReadResult> ReadBestAvailableAsync(
            ShotRecord shot,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeAssetOpener : IShotAssetOpener, IShotAssetPathResolver
    {
        public ShotRecord? Opened { get; private set; }
        public ShotRecord? Resolved { get; private set; }

        public Task OpenOriginalAsync(
            ShotRecord shot,
            CancellationToken cancellationToken = default)
        {
            Opened = shot;
            return Task.CompletedTask;
        }

        public Task<string> ResolveOriginalPathAsync(
            ShotRecord shot,
            CancellationToken cancellationToken = default)
        {
            Resolved = shot;
            return Task.FromResult(@"C:\Index\originals\shot.png");
        }
    }

    private sealed class FakeUriOpener : IExternalUriOpener
    {
        public List<Uri> Opened { get; } = [];
        public void Open(Uri uri) => Opened.Add(uri);
    }

    private sealed class FakeClipboard : IClipboardWriter
    {
        public byte[]? Png { get; private set; }
        public string? Text { get; private set; }
        public void WriteText(string text) => Text = text;
        public ValueTask WritePngAsync(
            ReadOnlyMemory<byte> pngData,
            CancellationToken cancellationToken = default)
        {
            Png = pngData.ToArray();
            return ValueTask.CompletedTask;
        }
        public ValueTask WriteFilesAsync(
            IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FakeExporter : IImageExporter
    {
        public byte[]? Png { get; private set; }
        public string? SuggestedName { get; private set; }
        public ValueTask<ImageExportResult> ExportPngAsync(
            ReadOnlyMemory<byte> png,
            string? suggestedName = null,
            CancellationToken cancellationToken = default)
        {
            Png = png.ToArray();
            SuggestedName = suggestedName;
            return ValueTask.FromResult(new ImageExportResult("export.png"));
        }
    }
}
