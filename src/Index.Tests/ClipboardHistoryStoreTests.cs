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
