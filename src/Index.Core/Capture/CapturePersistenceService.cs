using Index.Platform;
using Index.Storage;

namespace Index.Capture;

/// <summary>Everything needed to persist one selected capture without exposing storage schema to orchestration.</summary>
public sealed record CapturePersistenceRequest(
    PreparedCaptureImage Prepared,
    CaptureSelection Selection,
    DisplaySnapshot Display,
    SourceWindowBounds GlobalRegion,
    SourceApplicationSnapshot SourceSnapshot,
    DateTimeOffset CapturedAt);

public interface ICapturePersistenceService
{
    Task<StoredCapture> SaveAsync(
        CapturePersistenceRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves source attribution and writes the original plus its initial annotation revision.
/// Browser metadata and database details stay out of the capture session coordinator.
/// </summary>
public sealed class CapturePersistenceService : ICapturePersistenceService
{
    private readonly IShotCaptureWriter _writer;
    private readonly ISourceApplicationResolver _sourceApplicationResolver;
    private readonly IBrowserSourceMetadataResolver _browserSourceMetadataResolver;

    public CapturePersistenceService(
        IShotCaptureWriter writer,
        ISourceApplicationResolver sourceApplicationResolver,
        IBrowserSourceMetadataResolver browserSourceMetadataResolver)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _sourceApplicationResolver = sourceApplicationResolver
            ?? throw new ArgumentNullException(nameof(sourceApplicationResolver));
        _browserSourceMetadataResolver = browserSourceMetadataResolver
            ?? throw new ArgumentNullException(nameof(browserSourceMetadataResolver));
    }

    public async Task<StoredCapture> SaveAsync(
        CapturePersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = _sourceApplicationResolver.Resolve(
            request.SourceSnapshot,
            request.GlobalRegion);
        var sourceUrl = await _browserSourceMetadataResolver
            .ResolveUrlAsync(application, cancellationToken)
            .ConfigureAwait(false);

        return await _writer.SaveCaptureAsync(
            request.Prepared.BasePng,
            new ShotCaptureMetadata
            {
                CapturedAt = request.CapturedAt,
                Scale = request.Display.DpiScale,
                AppName = application?.AppName,
                AppIdentifier = application?.AppIdentifier,
                WindowTitle = application?.WindowTitle,
                SourceUrl = sourceUrl,
                DisplayIndex = request.Display.DisplayIndex,
                DisplayName = request.Display.DeviceName,
                RegionX = request.GlobalRegion.Left,
                RegionY = request.GlobalRegion.Top,
                RegionWidth = request.Selection.Width,
                RegionHeight = request.Selection.Height
            },
            request.Selection.Layers,
            cancellationToken).ConfigureAwait(false);
    }
}
