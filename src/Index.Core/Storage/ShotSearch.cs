namespace Index.Storage;

/// <summary>截图全文搜索的稳定边界；UI 不需要知道 FTS 表或属性存储结构。</summary>
public interface IShotSearchSource
{
    Task<IReadOnlyList<ShotRecord>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

public sealed partial class ShotStore
{
    public async Task<IReadOnlyList<ShotRecord>> SearchAsync(
        string? query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        var normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return await GetRecentAsync(limit, cancellationToken);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var useFts = normalized.EnumerateRunes().Count() >= 3;
        command.CommandText = useFts ? FtsSearchSql : LikeSearchSql;
        command.Parameters.AddWithValue("$limit", limit);
        if (useFts)
            command.Parameters.AddWithValue("$pattern", QuoteFtsPhrase(normalized));
        else
            command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(normalized)}%");

        var results = new List<ShotRecord>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadShot(reader));
        return results;
    }

    private const string ShotColumns = """
        shot.id, shot.sha256, shot.capturedAt, shot.pixelWidth, shot.pixelHeight, shot.scale,
        shot.appName, shot.appIdentifier, shot.windowTitle, shot.sourceURL,
        shot.displayIndex, shot.displayName, shot.regionX, shot.regionY, shot.regionWidth,
        shot.regionHeight, shot.originalExtension
        """;

    private const string FtsSearchSql = """
        SELECT
        """ + " " + ShotColumns + """
         FROM shot
        WHERE shot.id IN (
            SELECT rowid FROM shotFts WHERE shotFts MATCH $pattern
            UNION
            SELECT attribute.shotID
            FROM attributeFts
            JOIN shotAttribute AS attribute ON attribute.id = attributeFts.rowid
            WHERE attributeFts MATCH $pattern
        )
        ORDER BY shot.capturedAt DESC, shot.id DESC
        LIMIT $limit
        """;

    private const string LikeSearchSql = """
        SELECT
        """ + " " + ShotColumns + """
         FROM shot
        WHERE COALESCE(shot.appName, '') LIKE $pattern ESCAPE '\'
           OR COALESCE(shot.windowTitle, '') LIKE $pattern ESCAPE '\'
           OR COALESCE(shot.sourceURL, '') LIKE $pattern ESCAPE '\'
           OR EXISTS (
                SELECT 1 FROM shotAttribute AS attribute
                WHERE attribute.shotID = shot.id
                  AND COALESCE(attribute.text, '') LIKE $pattern ESCAPE '\'
           )
        ORDER BY shot.capturedAt DESC, shot.id DESC
        LIMIT $limit
        """;

    private static string QuoteFtsPhrase(string value)
        => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
