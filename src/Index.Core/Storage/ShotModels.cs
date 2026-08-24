using Index.Annotation;

namespace Index.Storage;

public sealed record ShotRecord(
    long Id,
    string Sha256,
    DateTimeOffset CapturedAt,
    int PixelWidth,
    int PixelHeight,
    double Scale,
    string? AppName,
    string? AppIdentifier,
    string? WindowTitle,
    string? SourceUrl,
    int? DisplayIndex,
    string? DisplayName,
    int RegionX,
    int RegionY,
    int RegionWidth,
    int RegionHeight,
    string OriginalExtension);

public sealed record RevisionRecord(
    long Id,
    long ShotId,
    long? ParentId,
    DateTimeOffset CreatedAt,
    string? Note,
    string LayersJson);

public sealed record ShotCaptureMetadata
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public double Scale { get; init; } = 1;
    public string? AppName { get; init; }
    public string? AppIdentifier { get; init; }
    public string? WindowTitle { get; init; }
    public string? SourceUrl { get; init; }
    public int? DisplayIndex { get; init; }
    public string? DisplayName { get; init; }
    public required int RegionX { get; init; }
    public required int RegionY { get; init; }
    public required int RegionWidth { get; init; }
    public required int RegionHeight { get; init; }
}

public sealed record StoredCapture(ShotRecord Shot, RevisionRecord LatestRevision);

public readonly record struct ShotPageCursor(DateTimeOffset CapturedAt, long Id);

public sealed record ShotPage(
    IReadOnlyList<ShotRecord> Items,
    ShotPageCursor? NextCursor);

public interface IShotCaptureWriter
{
    Task<StoredCapture> SaveCaptureAsync(
        ReadOnlyMemory<byte> originalPng,
        ShotCaptureMetadata metadata,
        Layers<ImageSpace> layers,
        CancellationToken cancellationToken = default);
}

public interface IShotStore : IShotCaptureWriter
{
    Task<IReadOnlyList<ShotRecord>> GetRecentAsync(
        int limit = 300,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    Task<ShotPage> GetPageAsync(
        int limit = 300,
        ShotPageCursor? cursor = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShotRecord>> GetByIdsAsync(
        IReadOnlyCollection<long> shotIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RevisionRecord>> GetRevisionsAsync(
        long shotId,
        CancellationToken cancellationToken = default);

    Task<RevisionRecord> AppendRevisionAsync(
        long shotId,
        Layers<ImageSpace> layers,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(long shotId, CancellationToken cancellationToken = default);
}
