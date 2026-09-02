using System.Diagnostics;
using Index.Storage;

namespace Index.Platform;

public sealed class WindowsShotAssetOpener : IShotAssetOpener
{
    private readonly ShotStore _store;
    private readonly IShotAssetReader _assets;

    public WindowsShotAssetOpener(ShotStore store, IShotAssetReader assets)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public async Task OpenOriginalAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);

        var asset = await _assets.ReadBestAvailableAsync(shot, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var originalPath = _store.OriginalPath(shot);

        switch (asset.Status)
        {
            case ShotAssetStatus.Original when asset.HasData:
                Process.Start(new ProcessStartInfo(originalPath)
                {
                    UseShellExecute = true
                });
                return;
            case ShotAssetStatus.ThumbnailFallback when File.Exists(originalPath):
                throw new InvalidDataException(asset.Warning ?? "The original image is corrupt.");
            case ShotAssetStatus.ThumbnailFallback:
                throw new FileNotFoundException(asset.Warning ?? "The original image is missing.");
            case ShotAssetStatus.Corrupt:
                throw new InvalidDataException(asset.Warning ?? "The original image is corrupt.");
            default:
                throw new FileNotFoundException(asset.Warning ?? "The image asset is missing.");
        }
    }
}
