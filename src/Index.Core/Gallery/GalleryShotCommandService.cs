using Index.Platform;
using Index.Platform.Clipboard;
using Index.Platform.Export;
using Index.Storage;

namespace Index.Gallery;

/// <summary>
/// Application service for single-shot gallery commands. It coordinates domain records and
/// narrow side-effect ports without exposing storage paths or platform UI to views.
/// </summary>
public sealed class GalleryShotCommandService
{
    private readonly IShotDeletionRepository _shots;
    private readonly IShotOrganizationRepository _organization;
    private readonly IShotAssetReader _assets;
    private readonly IShotAssetOpener _assetOpener;
    private readonly IExternalUriOpener _uriOpener;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly IImageExporter _exporter;

    public GalleryShotCommandService(
        IShotDeletionRepository shots,
        IShotOrganizationRepository organization,
        IShotAssetReader assets,
        IShotAssetOpener assetOpener,
        IExternalUriOpener uriOpener,
        IClipboardWriter clipboardWriter,
        IImageExporter exporter)
    {
        _shots = shots ?? throw new ArgumentNullException(nameof(shots));
        _organization = organization ?? throw new ArgumentNullException(nameof(organization));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _assetOpener = assetOpener ?? throw new ArgumentNullException(nameof(assetOpener));
        _uriOpener = uriOpener ?? throw new ArgumentNullException(nameof(uriOpener));
        _clipboardWriter = clipboardWriter
            ?? throw new ArgumentNullException(nameof(clipboardWriter));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public async Task CopyAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        var asset = await ReadPngAsync(shot, cancellationToken).ConfigureAwait(false);
        await _clipboardWriter.WritePngAsync(asset.Data, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> ExportAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        var asset = await ReadPngAsync(shot, cancellationToken).ConfigureAwait(false);
        var name = $"Index_{shot.CapturedAt.ToLocalTime():yyyyMMdd_HHmmss}";
        var result = await _exporter.ExportPngAsync(asset.Data, name, cancellationToken)
            .ConfigureAwait(false);
        return result.Path;
    }

    public Task OpenOriginalAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return _assetOpener.OpenOriginalAsync(shot, cancellationToken);
    }

    public bool OpenSource(ShotRecord shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        if (!Uri.TryCreate(shot.SourceUrl, UriKind.Absolute, out var source)
            || source.Scheme is not ("http" or "https"))
        {
            return false;
        }

        _uriOpener.Open(source);
        return true;
    }

    public Task<bool> DeleteAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return _shots.DeleteAsync(shot.Id, cancellationToken);
    }

    public Task<bool> IsFavoriteAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return _organization.IsFavoriteAsync(shot.Id, cancellationToken);
    }

    public Task SetFavoriteAsync(
        ShotRecord shot,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return _organization.SetFavoriteAsync(shot.Id, isFavorite, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetTagsAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);
        return _organization.GetTagsAsync(shot.Id, cancellationToken);
    }

    private async Task<ShotAssetReadResult> ReadPngAsync(
        ShotRecord shot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shot);
        var asset = await _assets.ReadBestAvailableAsync(shot, cancellationToken)
            .ConfigureAwait(false);
        if (asset.HasData
            && string.Equals(asset.MediaType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return asset;
        }

        if (asset.HasData)
            throw new InvalidDataException($"Unsupported image media type: {asset.MediaType}.");

        throw asset.Status == ShotAssetStatus.Corrupt
            ? new InvalidDataException(asset.Warning ?? "The image asset is corrupt.")
            : new FileNotFoundException(asset.Warning ?? "The image asset is missing.");
    }
}
