using System.Globalization;
using System.Text.Json;
using Index.Clipboard;
using Microsoft.Data.Sqlite;

namespace Index.Storage;

/// <summary>SQLite 剪切板历史仓储；图片资产按内容哈希存放在独立目录。</summary>
public sealed class ClipboardHistoryStore : IClipboardHistoryStore
{
    private readonly IndexDatabase _database;
    private readonly string _assetsDirectory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private ClipboardHistoryStore(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        _assetsDirectory = Path.Combine(root, "clipboard-assets");
        Directory.CreateDirectory(_assetsDirectory);
        _database = new IndexDatabase(Path.Combine(root, "index.sqlite"));
    }

    public static async Task<ClipboardHistoryStore> OpenAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var store = new ClipboardHistoryStore(rootDirectory);
        await store._database.InitializeAsync(cancellationToken);
        return store;
    }

    public static Task<ClipboardHistoryStore> OpenDefaultAsync(
        CancellationToken cancellationToken = default)
        => OpenAsync(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Index"),
            cancellationToken);

    public async Task<ClipboardHistoryItem> RecordAsync(
        ClipboardSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.ContentHash);

        string? assetPath = null;
        if (snapshot.Kind == ClipboardItemKind.Image && snapshot.ImagePng is { Length: > 0 })
        {
            assetPath = Path.Combine(_assetsDirectory, $"{snapshot.ContentHash}.png");
            if (!File.Exists(assetPath))
            {
                var stage = assetPath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllBytesAsync(stage, snapshot.ImagePng, cancellationToken);
                    try { File.Move(stage, assetPath, overwrite: false); }
                    catch (IOException) when (File.Exists(assetPath)) { }
                }
                finally
                {
                    if (File.Exists(stage)) File.Delete(stage);
                }
            }
        }

        var filePathsJson = snapshot.FilePaths is { Count: > 0 }
            ? JsonSerializer.Serialize(snapshot.FilePaths)
            : null;

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO clipboardItem(
                    kind, capturedAt, displayName, summary, textContent,
                    sourceApplication, contentHash, assetPath, filePathsJSON)
                VALUES(
                    $kind, $capturedAt, $displayName, $summary, $textContent,
                    $sourceApplication, $contentHash, $assetPath, $filePathsJSON)
                ON CONFLICT(contentHash) DO UPDATE SET
                    capturedAt = excluded.capturedAt,
                    displayName = excluded.displayName,
                    summary = excluded.summary,
                    textContent = excluded.textContent,
                    sourceApplication = excluded.sourceApplication,
                    assetPath = COALESCE(excluded.assetPath, clipboardItem.assetPath),
                    filePathsJSON = excluded.filePathsJSON
                RETURNING id, isPinned
                """;
            Add(command, "$kind", snapshot.Kind.ToString().ToLowerInvariant());
            Add(command, "$capturedAt", snapshot.CapturedAt.ToUniversalTime().ToString("O"));
            Add(command, "$displayName", snapshot.DisplayName);
            Add(command, "$summary", snapshot.Summary);
            Add(command, "$textContent", snapshot.Text);
            Add(command, "$sourceApplication", snapshot.SourceApplication);
            Add(command, "$contentHash", snapshot.ContentHash);
            Add(command, "$assetPath", assetPath);
            Add(command, "$filePathsJSON", filePathsJson);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("剪切板历史写入没有返回记录。");
            return new ClipboardHistoryItem(
                reader.GetInt64(0),
                snapshot.Kind,
                snapshot.CapturedAt,
                snapshot.DisplayName,
                snapshot.Summary,
                snapshot.Text,
                snapshot.SourceApplication,
                reader.GetInt64(1) != 0,
                assetPath,
                snapshot.FilePaths);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<ClipboardHistoryItem>> LoadRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) return [];
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, capturedAt, displayName, summary, textContent,
                   sourceApplication, isPinned, assetPath, filePathsJSON
            FROM clipboardItem
            ORDER BY isPinned DESC, capturedAt DESC, id DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ClipboardHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = Enum.Parse<ClipboardItemKind>(reader.GetString(1), ignoreCase: true);
            var filePaths = reader.IsDBNull(9)
                ? null
                : JsonSerializer.Deserialize<string[]>(reader.GetString(9));
            result.Add(new ClipboardHistoryItem(
                reader.GetInt64(0),
                kind,
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetString(4),
                NullableString(reader, 5),
                NullableString(reader, 6),
                reader.GetInt64(7) != 0,
                NullableString(reader, 8),
                filePaths));
        }
        return result;
    }

    public async Task TogglePinnedAsync(long id, CancellationToken cancellationToken = default)
    {
        await ExecuteWriteAsync(
            "UPDATE clipboardItem SET isPinned = CASE isPinned WHEN 0 THEN 1 ELSE 0 END WHERE id = $id",
            id,
            cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            string? assetPath;
            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT assetPath FROM clipboardItem WHERE id = $id";
                find.Parameters.AddWithValue("$id", id);
                assetPath = await find.ExecuteScalarAsync(cancellationToken) as string;
            }
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM clipboardItem WHERE id = $id";
                delete.Parameters.AddWithValue("$id", id);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            DeleteOwnedAsset(assetPath);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ExecuteWriteAsync(string sql, long id, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static void Add(SqliteCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? NullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private void DeleteOwnedAsset(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return;
        var fullPath = Path.GetFullPath(assetPath);
        var assetsRoot = Path.GetFullPath(_assetsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(fullPath)) File.Delete(fullPath);
    }
}
