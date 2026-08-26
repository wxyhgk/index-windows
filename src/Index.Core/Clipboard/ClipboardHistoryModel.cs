namespace Index.Clipboard;

/// <summary>Portable clipboard-history value types. No Win32 or WinUI dependencies belong here.</summary>
public enum ClipboardItemKind
{
    Text,
    Image,
    File
}

public sealed record ClipboardHistoryItem(
    long Id,
    ClipboardItemKind Kind,
    DateTimeOffset CapturedAt,
    string DisplayName,
    string Summary,
    string? Text,
    string? SourceApplication,
    bool IsPinned = false,
    string? AssetPath = null,
    IReadOnlyList<string>? FilePaths = null,
    DateTimeOffset? LastUsedAt = null,
    string? Title = null,
    bool IsFavorite = false)
{
    public string ResolvedDisplayName =>
        string.IsNullOrWhiteSpace(Title) ? DisplayName : Title;
}

/// <summary>Database-backed clipboard query. Null values mean that the filter is disabled.</summary>
public sealed record ClipboardHistoryQuery(
    string? SearchText = null,
    ClipboardItemKind? Kind = null,
    bool FavoritesOnly = false,
    int Limit = 100);

/// <summary>监听器交给存储层的一份完整、不可变剪切板快照。</summary>
public sealed record ClipboardSnapshot(
    ClipboardItemKind Kind,
    DateTimeOffset CapturedAt,
    string DisplayName,
    string Summary,
    string? Text,
    string? SourceApplication,
    string ContentHash,
    byte[]? ImagePng = null,
    IReadOnlyList<string>? FilePaths = null);

/// <summary>
/// Read seam for the popup. A SQLite repository can replace the demo source without changing the window.
/// </summary>
public interface IClipboardHistorySource
{
    Task<IReadOnlyList<ClipboardHistoryItem>> LoadRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<ClipboardHistoryItem>> SearchAsync(
        string? query,
        ClipboardItemKind? kind,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var items = await LoadRecentAsync(limit, cancellationToken);
        return items
            .Where(item => kind is null || item.Kind == kind)
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.ResolvedDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (item.Text?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.SourceApplication?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(Math.Max(0, limit))
            .ToArray();
    }
}

public interface IClipboardHistoryStore : IClipboardHistorySource
{
    Task<IReadOnlyList<ClipboardHistoryItem>> QueryAsync(
        ClipboardHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<ClipboardHistoryItem> RecordAsync(
        ClipboardSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task TogglePinnedAsync(long id, CancellationToken cancellationToken = default);
    Task MarkUsedAsync(long id, CancellationToken cancellationToken = default);
    Task SetTitleAsync(long id, string? title, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(long id, bool isFavorite, CancellationToken cancellationToken = default);
    Task<int> PruneAsync(
        int olderThanDays,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface IClipboardSnapshotReader
{
    Task<ClipboardSnapshot?> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IClipboardChangeWatcher : IDisposable
{
    event Action? Changed;
    void Start();
    void Stop();
}

/// <summary>Temporary in-memory data used while the watcher and database tables are still being ported.</summary>
public sealed class DemoClipboardHistorySource : IClipboardHistorySource
{
    public Task<IReadOnlyList<ClipboardHistoryItem>> LoadRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.Now;
        IReadOnlyList<ClipboardHistoryItem> items = new[]
        {
            new ClipboardHistoryItem(
                1, ClipboardItemKind.Text, now.AddMinutes(-1), "API 地址",
                "https://api.example.com/v1", "https://api.example.com/v1", "Microsoft Edge"),
            new ClipboardHistoryItem(
                2, ClipboardItemKind.Image, now.AddMinutes(-4), "设计稿截图",
                "图片 1440 x 900", null, "Figma"),
            new ClipboardHistoryItem(
                3, ClipboardItemKind.File, now.AddMinutes(-12), "release-notes.md",
                "1 个文件", null, "文件资源管理器", true),
            new ClipboardHistoryItem(
                4, ClipboardItemKind.Text, now.AddHours(-1), "命令",
                "dotnet test src/Index.sln", "dotnet test src/Index.sln", "Windows Terminal")
        }.Take(Math.Max(0, limit)).ToArray();
        return Task.FromResult(items);
    }
}
