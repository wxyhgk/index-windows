namespace Index.Storage;

/// <summary>
/// Opens the original asset through a platform adapter without exposing the
/// library's on-disk layout to the UI layer.
/// </summary>
public interface IShotAssetOpener
{
    Task OpenOriginalAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default);
}
