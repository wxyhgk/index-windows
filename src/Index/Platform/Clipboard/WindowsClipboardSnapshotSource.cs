using Index.Clipboard;
using Windows.ApplicationModel.DataTransfer;
using HistoryItem = Index.Clipboard.ClipboardHistoryItem;

namespace Index.Platform.Clipboard;

/// <summary>
/// One-shot Windows clipboard reader for the popup demo. It deliberately does not poll or persist yet.
/// </summary>
public sealed class WindowsClipboardSnapshotSource : IClipboardHistorySource
{
    public async Task<IReadOnlyList<HistoryItem>> LoadRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0) return Array.Empty<HistoryItem>();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            var now = DateTimeOffset.Now;
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                return new[]
                {
                    new HistoryItem(
                        1, ClipboardItemKind.Image, now, "当前剪贴板图片",
                        "图片预览将在监听阶段接入", null, null)
                };
            }

            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var files = await content.GetStorageItemsAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var first = files.FirstOrDefault()?.Name ?? "文件";
                return new[]
                {
                    new HistoryItem(
                        1, ClipboardItemKind.File, now, first,
                        $"{files.Count} 个文件", null, "文件资源管理器")
                };
            }

            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(text))
                {
                    var firstLine = text.Split('\n')[0].TrimEnd('\r');
                    var title = firstLine.Length <= 32 ? firstLine : firstLine[..32] + "…";
                    return new[]
                    {
                        new HistoryItem(
                            1, ClipboardItemKind.Text, now,
                            string.IsNullOrWhiteSpace(title) ? "当前文本" : title,
                            $"{text.Length} 个字符", text, null)
                    };
                }
            }
        }
        catch (Exception)
        {
            // Clipboard can be temporarily locked by another process. The popup remains usable and empty.
        }

        return Array.Empty<HistoryItem>();
    }
}
