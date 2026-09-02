using System.Globalization;

namespace Index.Storage;

public sealed partial class ShotStore
{
    private const string ApplicationIdentitySql = """
        'name:' || LOWER(TRIM(appName))
        """;

    public async Task<IReadOnlyList<CapturedApplicationSummary>> GetCapturedApplicationsAsync(
        int previewLimit = 3,
        CancellationToken cancellationToken = default)
    {
        if (previewLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(previewLimit));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH ranked AS (
                SELECT id, sha256, capturedAt, pixelWidth, pixelHeight, scale,
                       appName, appIdentifier, windowTitle, sourceURL,
                       displayIndex, displayName, regionX, regionY, regionWidth,
                       regionHeight, originalExtension,
                       {ApplicationIdentitySql} AS appIdentity,
                       ROW_NUMBER() OVER (
                           PARTITION BY {ApplicationIdentitySql}
                           ORDER BY capturedAt DESC, id DESC
                       ) AS appRank,
                       COUNT(*) OVER (
                           PARTITION BY {ApplicationIdentitySql}
                       ) AS captureCount,
                       MAX(capturedAt) OVER (
                           PARTITION BY {ApplicationIdentitySql}
                       ) AS lastCapturedAt
                FROM shot
                WHERE appName IS NOT NULL AND TRIM(appName) != ''
            )
            SELECT id, sha256, capturedAt, pixelWidth, pixelHeight, scale,
                   appName, appIdentifier, windowTitle, sourceURL,
                   displayIndex, displayName, regionX, regionY, regionWidth,
                   regionHeight, originalExtension, appIdentity, appRank,
                   captureCount, lastCapturedAt
            FROM ranked
            WHERE appRank <= $previewLimit
            ORDER BY captureCount DESC, appIdentity ASC, appRank ASC
            """;
        command.Parameters.AddWithValue("$previewLimit", previewLimit);

        var orderedIds = new List<string>();
        var builders = new Dictionary<string, ApplicationSummaryBuilder>(
            StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stableId = reader.GetString(17);
            if (!builders.TryGetValue(stableId, out var builder))
            {
                orderedIds.Add(stableId);
                builder = new ApplicationSummaryBuilder(
                    new CapturedApplicationIdentity(
                        reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7)),
                    checked((int)reader.GetInt64(19)),
                    DateTimeOffset.Parse(
                        reader.GetString(20),
                        CultureInfo.InvariantCulture),
                    []);
                builders.Add(stableId, builder);
            }

            builder.Previews.Add(ReadShot(reader));
        }

        return orderedIds.Select(stableId =>
        {
            var builder = builders[stableId];
            return new CapturedApplicationSummary(
                builder.Identity,
                builder.CaptureCount,
                builder.LastCapturedAt,
                builder.Previews.ToArray());
        }).ToArray();
    }

    public async Task<ShotPage> GetApplicationPageAsync(
        CapturedApplicationIdentity application,
        int limit = 300,
        ShotPageCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (limit <= 0)
            return new ShotPage([], null);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        const string identityPredicate = "TRIM(appName) COLLATE NOCASE = $appName";
        command.CommandText = $"""
            SELECT id, sha256, capturedAt, pixelWidth, pixelHeight, scale,
                   appName, appIdentifier, windowTitle, sourceURL,
                   displayIndex, displayName, regionX, regionY, regionWidth,
                   regionHeight, originalExtension
            FROM shot
            WHERE {identityPredicate}
              AND ($cursorCapturedAt IS NULL
                   OR capturedAt < $cursorCapturedAt
                   OR (capturedAt = $cursorCapturedAt AND id < $cursorId))
            ORDER BY capturedAt DESC, id DESC
            LIMIT $limit
            """;
        Add(command, "$appName", application.Name);
        Add(command, "$cursorCapturedAt", cursor?.CapturedAt.ToUniversalTime().ToString("O"));
        Add(command, "$cursorId", cursor?.Id);
        command.Parameters.AddWithValue("$limit", limit + 1);

        var result = new List<ShotRecord>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadShot(reader));
        var hasMore = result.Count > limit;
        if (hasMore)
            result.RemoveAt(result.Count - 1);
        ShotPageCursor? nextCursor = hasMore && result.Count > 0
            ? new ShotPageCursor(result[^1].CapturedAt, result[^1].Id)
            : null;
        return new ShotPage(result, nextCursor);
    }

    private sealed record ApplicationSummaryBuilder(
        CapturedApplicationIdentity Identity,
        int CaptureCount,
        DateTimeOffset LastCapturedAt,
        List<ShotRecord> Previews);
}
