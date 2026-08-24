using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Index.Storage;

public sealed record ShotCollectionRecord(
    long Id,
    string Name,
    string Note,
    long? CoverShotId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int SortOrder,
    int ItemCount);

/// <summary>
/// Persists user-owned library organization independently from gallery UI state.
/// Favorites and tags are attributes; collections are many-to-many references to shots.
/// </summary>
public sealed class LibraryOrganizationStore
{
    private const string FavoriteKey = "favorite";
    private const string TagKey = "tag";

    private readonly IndexDatabase _database;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private LibraryOrganizationStore(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        _database = new IndexDatabase(Path.Combine(root, "index.sqlite"));
    }

    public static async Task<LibraryOrganizationStore> OpenAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var store = new LibraryOrganizationStore(rootDirectory);
        await store._database.InitializeAsync(cancellationToken);
        return store;
    }

    public static Task<LibraryOrganizationStore> OpenDefaultAsync(
        CancellationToken cancellationToken = default)
        => OpenAsync(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Index"),
            cancellationToken);

    public async Task SetFavoriteAsync(
        long shotId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        await WithWriteGateAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            if (isFavorite)
            {
                command.CommandText = """
                    INSERT OR IGNORE INTO shotAttribute(shotID, key, payload, createdAt)
                    SELECT id, $key, X'01', $createdAt FROM shot WHERE id = $shotID
                    """;
                command.Parameters.AddWithValue("$createdAt", UtcNow());
            }
            else
            {
                command.CommandText =
                    "DELETE FROM shotAttribute WHERE shotID = $shotID AND key = $key";
            }
            command.Parameters.AddWithValue("$shotID", shotId);
            command.Parameters.AddWithValue("$key", FavoriteKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlySet<long>> GetFavoriteIdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT shotID FROM shotAttribute WHERE key = $key";
        command.Parameters.AddWithValue("$key", FavoriteKey);
        var result = new HashSet<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetInt64(0));
        return result;
    }

    public async Task AddTagAsync(
        long shotId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var tag = NormalizeName(name, nameof(name));
        await WithWriteGateAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO shotAttribute(shotID, key, text, createdAt)
                SELECT id, $key, $text, $createdAt FROM shot WHERE id = $shotID
                """;
            command.Parameters.AddWithValue("$shotID", shotId);
            command.Parameters.AddWithValue("$key", TagKey);
            command.Parameters.AddWithValue("$text", tag);
            command.Parameters.AddWithValue("$createdAt", UtcNow());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task RemoveTagAsync(
        long shotId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var tag = NormalizeName(name, nameof(name));
        await WithWriteGateAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM shotAttribute
                WHERE shotID = $shotID AND key = $key AND text = $text COLLATE NOCASE
                """;
            command.Parameters.AddWithValue("$shotID", shotId);
            command.Parameters.AddWithValue("$key", TagKey);
            command.Parameters.AddWithValue("$text", tag);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(
        long shotId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT text FROM shotAttribute
            WHERE shotID = $shotID AND key = $key AND text IS NOT NULL
            ORDER BY text COLLATE NOCASE, id
            """;
        command.Parameters.AddWithValue("$shotID", shotId);
        command.Parameters.AddWithValue("$key", TagKey);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<ShotCollectionRecord> CreateCollectionAsync(
        string name,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name, nameof(name));
        var normalizedNote = note?.Trim() ?? string.Empty;
        ShotCollectionRecord? created = null;
        try
        {
            await WithWriteGateAsync(async connection =>
            {
                var now = UtcNow();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO shotCollection(name, note, createdAt, updatedAt)
                    VALUES ($name, $note, $now, $now)
                    RETURNING id
                    """;
                command.Parameters.AddWithValue("$name", normalizedName);
                command.Parameters.AddWithValue("$note", normalizedNote);
                command.Parameters.AddWithValue("$now", now);
                var id = Convert.ToInt64(
                    await command.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
                var instant = DateTimeOffset.Parse(now, CultureInfo.InvariantCulture);
                created = new ShotCollectionRecord(
                    id, normalizedName, normalizedNote, null, instant, instant, 0, 0);
            }, cancellationToken);
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"Collection '{normalizedName}' already exists.", error);
        }
        return created!;
    }

    public async Task<IReadOnlyList<ShotCollectionRecord>> GetCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.name, c.note, c.coverShotID, c.createdAt, c.updatedAt,
                   c.sortOrder, COUNT(i.shotID)
            FROM shotCollection c
            LEFT JOIN shotCollectionItem i ON i.collectionID = c.id
            GROUP BY c.id
            ORDER BY c.sortOrder ASC, c.updatedAt DESC, c.id DESC
            """;
        var result = new List<ShotCollectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ShotCollectionRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.GetInt32(6),
                reader.GetInt32(7)));
        }
        return result;
    }

    public async Task AddToCollectionAsync(
        long collectionId,
        long shotId,
        CancellationToken cancellationToken = default)
    {
        await WithWriteGateAsync(async connection =>
        {
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            var now = UtcNow();
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT OR IGNORE INTO shotCollectionItem(collectionID, shotID, addedAt)
                    VALUES ($collectionID, $shotID, $addedAt)
                    """;
                insert.Parameters.AddWithValue("$collectionID", collectionId);
                insert.Parameters.AddWithValue("$shotID", shotId);
                insert.Parameters.AddWithValue("$addedAt", now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var touch = connection.CreateCommand())
            {
                touch.Transaction = transaction;
                touch.CommandText =
                    "UPDATE shotCollection SET updatedAt = $updatedAt WHERE id = $id";
                touch.Parameters.AddWithValue("$updatedAt", now);
                touch.Parameters.AddWithValue("$id", collectionId);
                await touch.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task RemoveFromCollectionAsync(
        long collectionId,
        long shotId,
        CancellationToken cancellationToken = default)
    {
        await WithWriteGateAsync(async connection =>
        {
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = """
                    DELETE FROM shotCollectionItem
                    WHERE collectionID = $collectionID AND shotID = $shotID
                    """;
                delete.Parameters.AddWithValue("$collectionID", collectionId);
                delete.Parameters.AddWithValue("$shotID", shotId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var touch = connection.CreateCommand())
            {
                touch.Transaction = transaction;
                touch.CommandText =
                    "UPDATE shotCollection SET updatedAt = $updatedAt WHERE id = $id";
                touch.Parameters.AddWithValue("$updatedAt", UtcNow());
                touch.Parameters.AddWithValue("$id", collectionId);
                await touch.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<long>> GetCollectionShotIdsAsync(
        long collectionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT shotID FROM shotCollectionItem
            WHERE collectionID = $collectionID
            ORDER BY sortOrder ASC, addedAt DESC, shotID DESC
            """;
        command.Parameters.AddWithValue("$collectionID", collectionId);
        var result = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetInt64(0));
        return result;
    }

    public async Task<bool> DeleteCollectionAsync(
        long collectionId,
        CancellationToken cancellationToken = default)
    {
        var deleted = false;
        await WithWriteGateAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM shotCollection WHERE id = $id";
            command.Parameters.AddWithValue("$id", collectionId);
            deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }, cancellationToken);
        return deleted;
    }

    private async Task WithWriteGateAsync(
        Func<SqliteConnection, Task> write,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await write(connection);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static string NormalizeName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > 120)
            throw new ArgumentOutOfRangeException(parameterName, "Name cannot exceed 120 characters.");
        return normalized;
    }

    private static string UtcNow() => DateTimeOffset.UtcNow.ToString("O");
}
