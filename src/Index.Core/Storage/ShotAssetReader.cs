namespace Index.Storage;

public enum ShotAssetStatus
{
    Original,
    ThumbnailFallback,
    Missing,
    Corrupt
}

public sealed record ShotAssetReadResult(
    ShotAssetStatus Status,
    ReadOnlyMemory<byte> Data,
    string? Warning = null,
    string MediaType = "image/png")
{
    public bool HasData => Status is ShotAssetStatus.Original or ShotAssetStatus.ThumbnailFallback
        && !Data.IsEmpty;
}

public interface IShotAssetReader
{
    Task<ShotAssetReadResult> ReadBestAvailableAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default);

    Task<ShotAssetReadResult> ReadPreviewAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
        => ReadBestAvailableAsync(shot, cancellationToken);

    /// <summary>Reads the original pixels with the latest complete annotation revision applied.</summary>
    Task<ShotAssetReadResult> ReadRenderedAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
        => ReadBestAvailableAsync(shot, cancellationToken);
}
