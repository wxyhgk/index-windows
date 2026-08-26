using System.Globalization;
using Index.Annotation;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace Index.Storage;

/// <summary>不可变原图文件与 Shot/Revision 数据库的统一入口。</summary>
public sealed partial class ShotStore : IShotStore, IShotSearchSource
{
    private readonly IndexDatabase _database;
    private readonly ShotFileStore _files;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _thumbnailMigrationGate = new(2, 2);

    private ShotStore(string rootDirectory)
    {
        _files = new ShotFileStore(rootDirectory);
        _database = new IndexDatabase(Path.Combine(_files.RootDirectory, "index.sqlite"));
    }

    public string RootDirectory => _files.RootDirectory;

    /// <summary>截图事务完整提交后触发；订阅者异常不会影响已经完成的保存。</summary>
    public event Action<StoredCapture>? CaptureSaved;

    public string OriginalPath(ShotRecord shot) => _files.OriginalPath(shot.Sha256);
    public string ThumbnailPath(ShotRecord shot) => _files.ThumbnailPath(shot.Sha256);
    public string LegacyThumbnailPath(ShotRecord shot) => _files.LegacyThumbnailPath(shot.Sha256);

    public async Task<bool> EnsureLosslessThumbnailAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shot);
        var target = ThumbnailPath(shot);
        if (File.Exists(target))
            return true;

        var original = OriginalPath(shot);
        if (!File.Exists(original))
            return false;

        await _thumbnailMigrationGate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(target))
                return true;
            var png = await File.ReadAllBytesAsync(original, cancellationToken);
            await Task.Run(
                () => _files.EnsureThumbnail(shot.Sha256, png),
                cancellationToken);
            return File.Exists(target);
        }
        finally
        {
            _thumbnailMigrationGate.Release();
        }
    }

    public static async Task<ShotStore> OpenAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var store = new ShotStore(rootDirectory);
        await store._database.InitializeAsync(cancellationToken);
        return store;
    }

    public static Task<ShotStore> OpenDefaultAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Index");
        return OpenAsync(root, cancellationToken);
    }

    public async Task<StoredCapture> SaveCaptureAsync(
        ReadOnlyMemory<byte> originalPng,
        ShotCaptureMetadata metadata,
        Layers<ImageSpace> layers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(layers);
        if (originalPng.IsEmpty)
            throw new ArgumentException("截图 PNG 不能为空。", nameof(originalPng));

        using var bitmap = SKBitmap.Decode(originalPng.ToArray())
            ?? throw new InvalidDataException("截图数据不是有效图片。");
        var sha256 = ShotFileStore.ComputeSha256(originalPng.Span);

        await _writeGate.WaitAsync(cancellationToken);
        var createdOriginal = false;
        try
        {
            createdOriginal = await _files.WriteOriginalAsync(
                sha256, originalPng, cancellationToken);
            _files.EnsureThumbnail(sha256, originalPng.Span);

            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                var shot = await InsertShotAsync(
                    connection, transaction, sha256, bitmap.Width, bitmap.Height,
                    metadata, cancellationToken);
                var initial = await InsertRevisionAsync(
                    connection, transaction, shot.Id, null, "[]", "原始",
                    metadata.CapturedAt, cancellationToken);
                var latest = initial;
                if (!layers.IsEmpty)
                {
                    latest = await InsertRevisionAsync(
                        connection, transaction, shot.Id, initial.Id,
                        LayerJson.Serialize(layers), "截图标注",
                        DateTimeOffset.UtcNow, cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                var stored = new StoredCapture(shot, latest);
                NotifyCaptureSaved(stored);
                return stored;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch
        {
            if (createdOriginal)
                _files.Delete(sha256);
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void NotifyCaptureSaved(StoredCapture capture)
    {
        if (CaptureSaved is not { } handlers) return;
        foreach (Action<StoredCapture> handler in handlers.GetInvocationList())
        {
            try { handler(capture); }
            catch { /* 保存已经提交，观察者故障不能把成功结果改成失败。 */ }
        }
    }

    public async Task<IReadOnlyList<ShotRecord>> GetRecentAsync(
        int limit = 300,
        CancellationToken cancellationToken = default)
        => (await GetPageAsync(limit, cancellationToken: cancellationToken)).Items;

    public async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM shot";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    public async Task<ShotPage> GetPageAsync(
        int limit = 300,
        ShotPageCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return new ShotPage([], null);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, sha256, capturedAt, pixelWidth, pixelHeight, scale,
                   appName, appIdentifier, windowTitle, sourceURL,
                   displayIndex, displayName, regionX, regionY, regionWidth,
                   regionHeight, originalExtension
            FROM shot
            WHERE $cursorCapturedAt IS NULL
               OR capturedAt < $cursorCapturedAt
               OR (capturedAt = $cursorCapturedAt AND id < $cursorId)
            ORDER BY capturedAt DESC, id DESC
            LIMIT $limit
            """;
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

    public async Task<IReadOnlyList<ShotRecord>> GetByIdsAsync(
        IReadOnlyCollection<long> shotIds,
        CancellationToken cancellationToken = default)
    {
        if (shotIds.Count == 0) return [];
        var ids = shotIds.Distinct().ToArray();
        var result = new List<ShotRecord>(ids.Length);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        for (var start = 0; start < ids.Length; start += 500)
        {
            var chunk = ids.Skip(start).Take(500).ToArray();
            await using var command = connection.CreateCommand();
            var parameters = new string[chunk.Length];
            for (var index = 0; index < chunk.Length; index++)
            {
                parameters[index] = $"$id{index}";
                command.Parameters.AddWithValue(parameters[index], chunk[index]);
            }
            command.CommandText = $"""
                SELECT id, sha256, capturedAt, pixelWidth, pixelHeight, scale,
                       appName, appIdentifier, windowTitle, sourceURL,
                       displayIndex, displayName, regionX, regionY, regionWidth,
                       regionHeight, originalExtension
                FROM shot
                WHERE id IN ({string.Join(",", parameters)})
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result.Add(ReadShot(reader));
        }
        return result
            .OrderByDescending(shot => shot.CapturedAt)
            .ThenByDescending(shot => shot.Id)
            .ToArray();
    }

    public async Task<IReadOnlyList<RevisionRecord>> GetRevisionsAsync(
        long shotId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, shotID, parentID, createdAt, note, layersJSON
            FROM revision WHERE shotID = $shotID ORDER BY id ASC
            """;
        command.Parameters.AddWithValue("$shotID", shotId);
        var result = new List<RevisionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadRevision(reader));
        return result;
    }

    public async Task<RevisionRecord> AppendRevisionAsync(
        long shotId,
        Layers<ImageSpace> layers,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layers);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            await using var parentCommand = connection.CreateCommand();
            parentCommand.Transaction = transaction;
            parentCommand.CommandText = "SELECT id FROM revision WHERE shotID = $shotID ORDER BY id DESC LIMIT 1";
            parentCommand.Parameters.AddWithValue("$shotID", shotId);
            var parentValue = await parentCommand.ExecuteScalarAsync(cancellationToken);
            if (parentValue is null)
                throw new KeyNotFoundException($"Shot {shotId} 不存在。 ");
            var parentId = Convert.ToInt64(parentValue, CultureInfo.InvariantCulture);
            var revision = await InsertRevisionAsync(
                connection, transaction, shotId, parentId, LayerJson.Serialize(layers),
                note, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return revision;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<bool> DeleteAsync(long shotId, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            await using var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = "SELECT sha256 FROM shot WHERE id = $id";
            find.Parameters.AddWithValue("$id", shotId);
            var sha256 = await find.ExecuteScalarAsync(cancellationToken) as string;
            if (sha256 is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM shot WHERE id = $id";
            delete.Parameters.AddWithValue("$id", shotId);
            await delete.ExecuteNonQueryAsync(cancellationToken);

            await using var count = connection.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM shot WHERE sha256 = $sha256";
            count.Parameters.AddWithValue("$sha256", sha256);
            var remaining = Convert.ToInt64(
                await count.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            await transaction.CommitAsync(cancellationToken);
            if (remaining == 0)
                _files.Delete(sha256);
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task<ShotRecord> InsertShotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sha256,
        int pixelWidth,
        int pixelHeight,
        ShotCaptureMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO shot (
                sha256, capturedAt, pixelWidth, pixelHeight, scale,
                appName, appIdentifier, windowTitle, sourceURL,
                displayIndex, displayName, regionX, regionY,
                regionWidth, regionHeight, originalExtension
            ) VALUES (
                $sha256, $capturedAt, $pixelWidth, $pixelHeight, $scale,
                $appName, $appIdentifier, $windowTitle, $sourceURL,
                $displayIndex, $displayName, $regionX, $regionY,
                $regionWidth, $regionHeight, 'png'
            ) RETURNING id
            """;
        Add(command, "$sha256", sha256);
        Add(command, "$capturedAt", metadata.CapturedAt.ToUniversalTime().ToString("O"));
        Add(command, "$pixelWidth", pixelWidth);
        Add(command, "$pixelHeight", pixelHeight);
        Add(command, "$scale", metadata.Scale);
        Add(command, "$appName", metadata.AppName);
        Add(command, "$appIdentifier", metadata.AppIdentifier);
        Add(command, "$windowTitle", metadata.WindowTitle);
        Add(command, "$sourceURL", metadata.SourceUrl);
        Add(command, "$displayIndex", metadata.DisplayIndex);
        Add(command, "$displayName", metadata.DisplayName);
        Add(command, "$regionX", metadata.RegionX);
        Add(command, "$regionY", metadata.RegionY);
        Add(command, "$regionWidth", metadata.RegionWidth);
        Add(command, "$regionHeight", metadata.RegionHeight);
        var id = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return new ShotRecord(
            id, sha256, metadata.CapturedAt, pixelWidth, pixelHeight, metadata.Scale,
            metadata.AppName, metadata.AppIdentifier, metadata.WindowTitle,
            metadata.SourceUrl, metadata.DisplayIndex, metadata.DisplayName,
            metadata.RegionX, metadata.RegionY, metadata.RegionWidth,
            metadata.RegionHeight, "png");
    }

    private static async Task<RevisionRecord> InsertRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long shotId,
        long? parentId,
        string layersJson,
        string? note,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO revision(shotID, parentID, createdAt, note, layersJSON)
            VALUES ($shotID, $parentID, $createdAt, $note, $layersJSON)
            RETURNING id
            """;
        Add(command, "$shotID", shotId);
        Add(command, "$parentID", parentId);
        Add(command, "$createdAt", createdAt.ToUniversalTime().ToString("O"));
        Add(command, "$note", note);
        Add(command, "$layersJSON", layersJson);
        var id = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return new RevisionRecord(id, shotId, parentId, createdAt, note, layersJson);
    }

    private static void Add(SqliteCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static ShotRecord ReadShot(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
        reader.GetInt32(3), reader.GetInt32(4), reader.GetDouble(5),
        NullableString(reader, 6), NullableString(reader, 7), NullableString(reader, 8), NullableString(reader, 9),
        reader.IsDBNull(10) ? null : reader.GetInt32(10), NullableString(reader, 11),
        reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetString(16));

    private static RevisionRecord ReadRevision(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
        NullableString(reader, 4), reader.GetString(5));

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
