namespace Index.Storage;

public enum ShotAssetStatus
{
    Original,
    ThumbnailFallback,
    Missing
}

public sealed record ShotAssetReadResult(
    ShotAssetStatus Status,
    ReadOnlyMemory<byte> Data,
    string? Warning = null)
{
    public bool HasData => Status is not ShotAssetStatus.Missing && !Data.IsEmpty;
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
}
