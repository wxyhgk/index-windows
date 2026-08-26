using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Index.Storage;

/// <summary>SQLite 连接配置和顺序迁移。每个连接都显式启用外键。</summary>
public sealed class IndexDatabase
{
    private readonly string _connectionString;

    public IndexDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS schemaMigration (
                name TEXT PRIMARY KEY NOT NULL,
                appliedAt TEXT NOT NULL
            );
            """, cancellationToken);

        if (!await MigrationAppliedAsync(connection, "v1_shots", cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE shot (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sha256 TEXT NOT NULL,
                    capturedAt TEXT NOT NULL,
                    pixelWidth INTEGER NOT NULL,
                    pixelHeight INTEGER NOT NULL,
                    scale REAL NOT NULL DEFAULT 1.0,
                    appName TEXT,
                    appIdentifier TEXT,
                    windowTitle TEXT,
                    sourceURL TEXT,
                    displayIndex INTEGER,
                    displayName TEXT,
                    regionX INTEGER NOT NULL DEFAULT 0,
                    regionY INTEGER NOT NULL DEFAULT 0,
                    regionWidth INTEGER NOT NULL DEFAULT 0,
                    regionHeight INTEGER NOT NULL DEFAULT 0,
                    originalExtension TEXT NOT NULL DEFAULT 'png'
                );
                CREATE INDEX index_shot_on_sha256 ON shot(sha256);
                CREATE INDEX index_shot_on_capturedAt_id ON shot(capturedAt DESC, id DESC);

                CREATE TABLE revision (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    shotID INTEGER NOT NULL,
                    parentID INTEGER,
                    createdAt TEXT NOT NULL,
                    note TEXT,
                    layersJSON TEXT NOT NULL DEFAULT '[]',
                    FOREIGN KEY (shotID) REFERENCES shot(id) ON DELETE CASCADE,
                    FOREIGN KEY (parentID) REFERENCES revision(id) ON DELETE SET NULL
                );
                CREATE INDEX index_revision_on_shotID_id ON revision(shotID, id DESC);
                """, cancellationToken);
            await MarkMigrationAsync(connection, transaction, "v1_shots", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (!await MigrationAppliedAsync(connection, "v2_clipboard_history", cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE clipboardItem (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    kind TEXT NOT NULL,
                    capturedAt TEXT NOT NULL,
                    displayName TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    textContent TEXT,
                    sourceApplication TEXT,
                    contentHash TEXT NOT NULL,
                    assetPath TEXT,
                    filePathsJSON TEXT,
                    isPinned INTEGER NOT NULL DEFAULT 0
                );
                CREATE UNIQUE INDEX index_clipboardItem_on_contentHash
                    ON clipboardItem(contentHash);
                CREATE INDEX index_clipboardItem_on_pinned_capturedAt
                    ON clipboardItem(isPinned DESC, capturedAt DESC, id DESC);
                """, cancellationToken);
            await MarkMigrationAsync(
                connection,
                transaction,
                "v2_clipboard_history",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (!await MigrationAppliedAsync(connection, "v3_library_organization", cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, """
                CREATE TABLE shotAttribute (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    shotID INTEGER NOT NULL,
                    key TEXT NOT NULL,
                    text TEXT,
                    payload BLOB,
                    createdAt TEXT NOT NULL,
                    FOREIGN KEY (shotID) REFERENCES shot(id) ON DELETE CASCADE
                );
                CREATE INDEX index_shotAttribute_on_shotID_key
                    ON shotAttribute(shotID, key);
                CREATE INDEX index_shotAttribute_on_key_shotID
                    ON shotAttribute(key, shotID);
                CREATE UNIQUE INDEX index_shotAttribute_unique_favorite
                    ON shotAttribute(shotID, key) WHERE key = 'favorite';
                CREATE UNIQUE INDEX index_shotAttribute_unique_tag
                    ON shotAttribute(shotID, key, text COLLATE NOCASE) WHERE key = 'tag';

                CREATE TABLE shotCollection (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL COLLATE NOCASE,
                    note TEXT NOT NULL DEFAULT '',
                    coverShotID INTEGER,
                    createdAt TEXT NOT NULL,
                    updatedAt TEXT NOT NULL,
                    sortOrder INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (coverShotID) REFERENCES shot(id) ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX index_shotCollection_on_name
                    ON shotCollection(name COLLATE NOCASE);
                CREATE INDEX index_shotCollection_on_sortOrder_updatedAt
                    ON shotCollection(sortOrder ASC, updatedAt DESC, id DESC);

                CREATE TABLE shotCollectionItem (
                    collectionID INTEGER NOT NULL,
                    shotID INTEGER NOT NULL,
                    addedAt TEXT NOT NULL,
                    sortOrder INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (collectionID, shotID),
                    FOREIGN KEY (collectionID) REFERENCES shotCollection(id) ON DELETE CASCADE,
                    FOREIGN KEY (shotID) REFERENCES shot(id) ON DELETE CASCADE
                );
                CREATE INDEX index_shotCollectionItem_on_shotID
                    ON shotCollectionItem(shotID);
                CREATE INDEX index_shotCollectionItem_on_collection_order
                    ON shotCollectionItem(collectionID, sortOrder ASC, addedAt DESC);
                """, cancellationToken);
            await MarkMigrationAsync(
                connection,
                transaction,
                "v3_library_organization",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (!await MigrationAppliedAsync(connection, "v4_search_and_clipboard_metadata", cancellationToken))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, """
                ALTER TABLE clipboardItem ADD COLUMN lastUsedAt TEXT;
                ALTER TABLE clipboardItem ADD COLUMN title TEXT;
                ALTER TABLE clipboardItem ADD COLUMN isFavorite INTEGER NOT NULL DEFAULT 0;

                CREATE INDEX index_clipboardItem_on_favorite_capturedAt
                    ON clipboardItem(isFavorite DESC, capturedAt DESC, id DESC);
                CREATE INDEX index_clipboardItem_on_kind_capturedAt
                    ON clipboardItem(kind, capturedAt DESC, id DESC);

                CREATE VIRTUAL TABLE shotFts USING fts5(
                    appName,
                    windowTitle,
                    sourceURL,
                    content='shot',
                    content_rowid='id',
                    tokenize='trigram'
                );
                INSERT INTO shotFts(shotFts) VALUES('rebuild');
                CREATE TRIGGER shotFts_after_insert AFTER INSERT ON shot BEGIN
                    INSERT INTO shotFts(rowid, appName, windowTitle, sourceURL)
                    VALUES (new.id, new.appName, new.windowTitle, new.sourceURL);
                END;
                CREATE TRIGGER shotFts_after_delete AFTER DELETE ON shot BEGIN
                    INSERT INTO shotFts(shotFts, rowid, appName, windowTitle, sourceURL)
                    VALUES ('delete', old.id, old.appName, old.windowTitle, old.sourceURL);
                END;
                CREATE TRIGGER shotFts_after_update AFTER UPDATE OF appName, windowTitle, sourceURL ON shot BEGIN
                    INSERT INTO shotFts(shotFts, rowid, appName, windowTitle, sourceURL)
                    VALUES ('delete', old.id, old.appName, old.windowTitle, old.sourceURL);
                    INSERT INTO shotFts(rowid, appName, windowTitle, sourceURL)
                    VALUES (new.id, new.appName, new.windowTitle, new.sourceURL);
                END;

                CREATE VIRTUAL TABLE attributeFts USING fts5(
                    shotID UNINDEXED,
                    key UNINDEXED,
                    text,
                    content='shotAttribute',
                    content_rowid='id',
                    tokenize='trigram'
                );
                INSERT INTO attributeFts(attributeFts) VALUES('rebuild');
                CREATE TRIGGER attributeFts_after_insert AFTER INSERT ON shotAttribute BEGIN
                    INSERT INTO attributeFts(rowid, shotID, key, text)
                    VALUES (new.id, new.shotID, new.key, new.text);
                END;
                CREATE TRIGGER attributeFts_after_delete AFTER DELETE ON shotAttribute BEGIN
                    INSERT INTO attributeFts(attributeFts, rowid, shotID, key, text)
                    VALUES ('delete', old.id, old.shotID, old.key, old.text);
                END;
                CREATE TRIGGER attributeFts_after_update AFTER UPDATE OF shotID, key, text ON shotAttribute BEGIN
                    INSERT INTO attributeFts(attributeFts, rowid, shotID, key, text)
                    VALUES ('delete', old.id, old.shotID, old.key, old.text);
                    INSERT INTO attributeFts(rowid, shotID, key, text)
                    VALUES (new.id, new.shotID, new.key, new.text);
                END;

                CREATE VIRTUAL TABLE clipboardFts USING fts5(
                    displayName,
                    summary,
                    textContent,
                    sourceApplication,
                    title,
                    content='clipboardItem',
                    content_rowid='id',
                    tokenize='trigram'
                );
                INSERT INTO clipboardFts(clipboardFts) VALUES('rebuild');
                CREATE TRIGGER clipboardFts_after_insert AFTER INSERT ON clipboardItem BEGIN
                    INSERT INTO clipboardFts(
                        rowid, displayName, summary, textContent, sourceApplication, title)
                    VALUES (
                        new.id, new.displayName, new.summary, new.textContent,
                        new.sourceApplication, new.title);
                END;
                CREATE TRIGGER clipboardFts_after_delete AFTER DELETE ON clipboardItem BEGIN
                    INSERT INTO clipboardFts(
                        clipboardFts, rowid, displayName, summary, textContent,
                        sourceApplication, title)
                    VALUES (
                        'delete', old.id, old.displayName, old.summary, old.textContent,
                        old.sourceApplication, old.title);
                END;
                CREATE TRIGGER clipboardFts_after_update
                AFTER UPDATE OF displayName, summary, textContent, sourceApplication, title
                ON clipboardItem BEGIN
                    INSERT INTO clipboardFts(
                        clipboardFts, rowid, displayName, summary, textContent,
                        sourceApplication, title)
                    VALUES (
                        'delete', old.id, old.displayName, old.summary, old.textContent,
                        old.sourceApplication, old.title);
                    INSERT INTO clipboardFts(
                        rowid, displayName, summary, textContent, sourceApplication, title)
                    VALUES (
                        new.id, new.displayName, new.summary, new.textContent,
                        new.sourceApplication, new.title);
                END;
                """, cancellationToken);
            await MarkMigrationAsync(
                connection,
                transaction,
                "v4_search_and_clipboard_metadata",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task<bool> MigrationAppliedAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM schemaMigration WHERE name = $name";
        check.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(
            await check.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) > 0;
    }

    private static async Task MarkMigrationAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var mark = connection.CreateCommand();
        mark.Transaction = (SqliteTransaction)transaction;
        mark.CommandText = "INSERT INTO schemaMigration(name, appliedAt) VALUES ($name, $at)";
        mark.Parameters.AddWithValue("$name", name);
        mark.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await mark.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, null, """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                """, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
