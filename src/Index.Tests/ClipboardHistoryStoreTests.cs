using Index.Clipboard;
using Index.Storage;
using Microsoft.Data.Sqlite;

namespace Index.Tests;

public sealed class ClipboardHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-clipboard-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RecordDeduplicatesByHashAndRefreshesMetadata()
    {
        var store = await ClipboardHistoryStore.OpenAsync(_root);
        var firstTime = new DateTimeOffset(2026, 8, 23, 1, 0, 0, TimeSpan.Zero);
        var secondTime = firstTime.AddMinutes(5);

        await store.RecordAsync(Text("same-hash", "第一次", "Terminal", firstTime));
        await store.RecordAsync(Text("same-hash", "第二次", "Editor", secondTime));

        var item = Assert.Single(await store.LoadRecentAsync());
        Assert.Equal("第二次", item.Text);
        Assert.Equal("Editor", item.SourceApplication);
        Assert.Equal(secondTime, item.CapturedAt);
    }

    [Fact]
    public async Task ImageAssetPinAndDeletePersist()
    {
        var store = await ClipboardHistoryStore.OpenAsync(_root);
        var saved = await store.RecordAsync(new ClipboardSnapshot(
            ClipboardItemKind.Image,
            DateTimeOffset.UtcNow,
            "图片",
            "图片 1 × 1",
            null,
            "Paint",
            "image-hash",
            [1, 2, 3, 4]));

        Assert.NotNull(saved.AssetPath);
        Assert.True(File.Exists(saved.AssetPath));
        await store.TogglePinnedAsync(saved.Id);
        Assert.True(Assert.Single(await store.LoadRecentAsync()).IsPinned);

        var assetPath = saved.AssetPath;
        await store.DeleteAsync(saved.Id);
        Assert.Empty(await store.LoadRecentAsync());
        Assert.False(File.Exists(assetPath));
    }

    [Fact]
    public async Task SearchRunsAcrossDatabaseAndAppliesKindFilter()
    {
        var store = await ClipboardHistoryStore.OpenAsync(_root);
        var start = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        await store.RecordAsync(Text("target-hash", "prefix-needle-suffix", "Editor", start));
        for (var index = 0; index < 60; index++)
            await store.RecordAsync(Text($"hash-{index}", $"ordinary-{index}", "Terminal", start.AddMinutes(index + 1)));

        var matches = await store.SearchAsync("needle", ClipboardItemKind.Text, 10);

        Assert.Equal("prefix-needle-suffix", Assert.Single(matches).Text);
        Assert.Empty(await store.SearchAsync("needle", ClipboardItemKind.Image, 10));
    }

    [Fact]
    public async Task UserMetadataPersistsWhenDuplicateContentIsRecordedAgain()
    {
        var store = await ClipboardHistoryStore.OpenAsync(_root);
        var first = await store.RecordAsync(Text(
            "same-content",
            "body",
            "Editor",
            DateTimeOffset.Parse("2026-08-20T00:00:00Z")));
        await store.SetTitleAsync(first.Id, "  Renamed item  ");
        await store.SetFavoriteAsync(first.Id, true);
        await store.MarkUsedAsync(first.Id);

        await store.RecordAsync(Text(
            "same-content",
            "updated body",
            "Terminal",
            DateTimeOffset.Parse("2026-08-21T00:00:00Z")));

        var item = Assert.Single(await store.QueryAsync(
            new ClipboardHistoryQuery("Renamed", FavoritesOnly: true)));
        Assert.Equal("Renamed item", item.Title);
        Assert.Equal("Renamed item", item.ResolvedDisplayName);
        Assert.True(item.IsFavorite);
        Assert.NotNull(item.LastUsedAt);
        Assert.Equal("updated body", item.Text);
    }

    [Fact]
    public async Task PruneDeletesOnlyExpiredUnprotectedItemsAndTheirAssets()
    {
        var store = await ClipboardHistoryStore.OpenAsync(_root);
        var now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var stale = now.AddDays(-31);
        var expiredText = await store.RecordAsync(Text("expired", "expired", "Editor", stale));
        var pinned = await store.RecordAsync(Text("pinned", "pinned", "Editor", stale));
        var favorite = await store.RecordAsync(Text("favorite", "favorite", "Editor", stale));
        var recent = await store.RecordAsync(Text("recent", "recent", "Editor", now.AddDays(-1)));
        var expiredImage = await store.RecordAsync(new ClipboardSnapshot(
            ClipboardItemKind.Image,
            stale,
            "old image",
            "old image",
            null,
            "Paint",
            "expired-image",
            [1, 2, 3]));
        await store.TogglePinnedAsync(pinned.Id);
        await store.SetFavoriteAsync(favorite.Id, true);

        var deleted = await store.PruneAsync(30, now);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(expiredImage.AssetPath));
        var remaining = await store.LoadRecentAsync(20);
        Assert.DoesNotContain(remaining, item => item.Id == expiredText.Id);
        Assert.Contains(remaining, item => item.Id == pinned.Id && item.IsPinned);
        Assert.Contains(remaining, item => item.Id == favorite.Id && item.IsFavorite);
        Assert.Contains(remaining, item => item.Id == recent.Id);
    }

    [Fact]
    public async Task SearchMigrationUpgradesAndBackfillsExistingRows()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "index.sqlite");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schemaMigration (name TEXT PRIMARY KEY NOT NULL, appliedAt TEXT NOT NULL);
                INSERT INTO schemaMigration(name, appliedAt) VALUES
                    ('v1_shots', '2026-08-20T00:00:00Z'),
                    ('v2_clipboard_history', '2026-08-20T00:00:00Z'),
                    ('v3_library_organization', '2026-08-20T00:00:00Z');
                CREATE TABLE shot (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, sha256 TEXT NOT NULL,
                    capturedAt TEXT NOT NULL, pixelWidth INTEGER NOT NULL,
                    pixelHeight INTEGER NOT NULL, scale REAL NOT NULL DEFAULT 1.0,
                    appName TEXT, appIdentifier TEXT, windowTitle TEXT, sourceURL TEXT,
                    displayIndex INTEGER, displayName TEXT, regionX INTEGER NOT NULL DEFAULT 0,
                    regionY INTEGER NOT NULL DEFAULT 0, regionWidth INTEGER NOT NULL DEFAULT 0,
                    regionHeight INTEGER NOT NULL DEFAULT 0, originalExtension TEXT NOT NULL DEFAULT 'png');
                CREATE TABLE shotAttribute (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, shotID INTEGER NOT NULL,
                    key TEXT NOT NULL, text TEXT, payload BLOB, createdAt TEXT NOT NULL);
                CREATE TABLE clipboardItem (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL,
                    capturedAt TEXT NOT NULL, displayName TEXT NOT NULL, summary TEXT NOT NULL,
                    textContent TEXT, sourceApplication TEXT, contentHash TEXT NOT NULL,
                    assetPath TEXT, filePathsJSON TEXT, isPinned INTEGER NOT NULL DEFAULT 0);
                INSERT INTO shot(sha256, capturedAt, pixelWidth, pixelHeight, appName, windowTitle)
                    VALUES ('sha', '2026-08-20T00:00:00Z', 1, 1, 'Legacy Editor', 'Legacy window');
                INSERT INTO shotAttribute(shotID, key, text, createdAt)
                    VALUES (1, 'ocr', 'legacy recognized phrase', '2026-08-20T00:00:00Z');
                INSERT INTO clipboardItem(
                    kind, capturedAt, displayName, summary, textContent, sourceApplication, contentHash)
                    VALUES ('text', '2026-08-20T00:00:00Z', 'Legacy clip', 'summary',
                            'legacy clipboard phrase', 'Editor', 'legacy-hash');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = await ClipboardHistoryStore.OpenAsync(_root);

        Assert.Single(await store.SearchAsync("clipboard", null, 10));
        await using var verify = new SqliteConnection($"Data Source={databasePath}");
        await verify.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(
            verify,
            "SELECT COUNT(*) FROM shotFts WHERE shotFts MATCH '\"Legacy\"'"));
        Assert.Equal(1L, await ScalarAsync(
            verify,
            "SELECT COUNT(*) FROM attributeFts WHERE attributeFts MATCH '\"recognized\"'"));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static ClipboardSnapshot Text(
        string hash,
        string text,
        string source,
        DateTimeOffset capturedAt) => new(
            ClipboardItemKind.Text,
            capturedAt,
            text,
            $"{text.Length} 个字符",
            text,
            source,
            hash);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
