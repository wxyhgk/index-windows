using Index.Clipboard;
using Index.Storage;

namespace Index.Search;

public enum UnifiedSearchEntryKind
{
    Screenshot,
    ClipboardText,
    ClipboardImage,
    ClipboardFile
}

/// <summary>截图和剪贴板记录的统一搜索投影。</summary>
public sealed record UnifiedSearchEntry(
    string Id,
    UnifiedSearchEntryKind Kind,
    string Title,
    string Subtitle,
    string Summary,
    DateTimeOffset Timestamp,
    ShotRecord? Shot = null,
    ClipboardHistoryItem? ClipboardItem = null);

public interface IUnifiedSearchService
{
    Task<IReadOnlyList<UnifiedSearchEntry>> SearchAsync(
        string? query,
        int limit = 30,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 并行查询截图与剪贴板，再按统一时间线合并。具体 FTS 语义留在各自仓储。
/// </summary>
public sealed class UnifiedSearchService : IUnifiedSearchService
{
    private readonly IShotSearchSource _shots;
    private readonly IClipboardHistorySource _clipboard;

    public UnifiedSearchService(
        IShotSearchSource shots,
        IClipboardHistorySource clipboard)
    {
        _shots = shots ?? throw new ArgumentNullException(nameof(shots));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public async Task<IReadOnlyList<UnifiedSearchEntry>> SearchAsync(
        string? query,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        var normalized = query?.Trim();
        var shotTask = _shots.SearchAsync(normalized, limit, cancellationToken);
        var clipboardTask = _clipboard.SearchAsync(
            normalized,
            kind: null,
            limit,
            cancellationToken);
        await Task.WhenAll(shotTask, clipboardTask);

        cancellationToken.ThrowIfCancellationRequested();
        return (await shotTask)
            .Select(FromShot)
            .Concat((await clipboardTask).Select(FromClipboard))
            .OrderByDescending(entry => entry.Timestamp)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static UnifiedSearchEntry FromShot(ShotRecord shot)
        => new(
            $"shot-{shot.Id}",
            UnifiedSearchEntryKind.Screenshot,
            shot.WindowTitle ?? shot.AppName ?? "截图",
            shot.AppName ?? shot.SourceUrl ?? string.Empty,
            shot.SourceUrl ?? $"{shot.PixelWidth} × {shot.PixelHeight}",
            shot.CapturedAt,
            Shot: shot);

    private static UnifiedSearchEntry FromClipboard(ClipboardHistoryItem item)
        => new(
            $"clipboard-{item.Id}",
            item.Kind switch
            {
                ClipboardItemKind.Text => UnifiedSearchEntryKind.ClipboardText,
                ClipboardItemKind.Image => UnifiedSearchEntryKind.ClipboardImage,
                _ => UnifiedSearchEntryKind.ClipboardFile
            },
            item.ResolvedDisplayName,
            item.SourceApplication ?? string.Empty,
            item.Text ?? item.Summary,
            item.CapturedAt,
            ClipboardItem: item);
}
