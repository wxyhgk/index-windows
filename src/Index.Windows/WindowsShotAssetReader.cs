using Index.Storage;

namespace Index.Platform;

public sealed class WindowsShotAssetReader : IShotAssetReader
{
    private readonly ShotStore _store;

    public WindowsShotAssetReader(ShotStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ShotAssetReadResult> ReadBestAvailableAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);

        var originalPath = _store.OriginalPath(shot);
        if (File.Exists(originalPath))
        {
            return new ShotAssetReadResult(
                ShotAssetStatus.Original,
                await File.ReadAllBytesAsync(originalPath, cancellationToken));
        }

        var thumbnailPath = FirstExistingThumbnail(shot);
        if (thumbnailPath is not null)
        {
            return new ShotAssetReadResult(
                ShotAssetStatus.ThumbnailFallback,
                await File.ReadAllBytesAsync(thumbnailPath, cancellationToken),
                "原图缺失，当前使用缩略图");
        }

        return new ShotAssetReadResult(
            ShotAssetStatus.Missing,
            ReadOnlyMemory<byte>.Empty,
            "原图和缩略图文件都不存在");
    }

    public async Task<ShotAssetReadResult> ReadPreviewAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);

        var thumbnailPath = FirstExistingThumbnail(shot);
        if (thumbnailPath is not null)
        {
            return new ShotAssetReadResult(
                ShotAssetStatus.ThumbnailFallback,
                await File.ReadAllBytesAsync(thumbnailPath, cancellationToken));
        }

        return await ReadBestAvailableAsync(shot, cancellationToken);
    }

    private string? FirstExistingThumbnail(ShotRecord shot)
    {
        var lossless = _store.ThumbnailPath(shot);
        if (File.Exists(lossless))
            return lossless;

        var legacy = _store.LegacyThumbnailPath(shot);
        return File.Exists(legacy) ? legacy : null;
    }
}
