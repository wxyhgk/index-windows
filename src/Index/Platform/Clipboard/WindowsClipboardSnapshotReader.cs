using Index.Clipboard;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Index.Platform.Clipboard;

/// <summary>把当前 Windows 剪切板解析为可持久化的便携快照。</summary>
public sealed class WindowsClipboardSnapshotReader : IClipboardSnapshotReader
{
    private readonly ISourceApplicationResolver _sourceResolver;

    public WindowsClipboardSnapshotReader(ISourceApplicationResolver sourceResolver)
    {
        _sourceResolver = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));
    }

    public async Task<ClipboardSnapshot?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            var source = _sourceResolver.CaptureSnapshot().ForegroundApplication?.AppName;
            var capturedAt = DateTimeOffset.UtcNow;

            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var bitmapReference = await content.GetBitmapAsync();
                using var stream = await bitmapReference.OpenReadAsync();
                if (stream.Size == 0 || stream.Size > int.MaxValue) return null;
                var bytes = new byte[checked((int)stream.Size)];
                using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync(checked((uint)stream.Size));
                    reader.ReadBytes(bytes);
                }

                var summary = "图片";
                using var image = SKBitmap.Decode(bytes);
                if (image is not null)
                    summary = $"图片 {image.Width} × {image.Height}";
                return new ClipboardSnapshot(
                    ClipboardItemKind.Image,
                    capturedAt,
                    "剪切板图片",
                    summary,
                    null,
                    source,
                    ClipboardContentHash.FromImagePng(bytes),
                    bytes);
            }

            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var paths = items.Select(item => item.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();
                if (paths.Length == 0) return null;
                return new ClipboardSnapshot(
                    ClipboardItemKind.File,
                    capturedAt,
                    Path.GetFileName(paths[0]),
                    $"{paths.Length} 个文件",
                    null,
                    source,
                    ClipboardContentHash.FromFiles(paths),
                    FilePaths: paths);
            }

            if (content.Contains(StandardDataFormats.Text))
            {
                var text = await content.GetTextAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(text)) return null;
                var firstLine = text.Split('\n', 2)[0].TrimEnd('\r').Trim();
                var title = string.IsNullOrEmpty(firstLine) ? "文本" : firstLine;
                if (title.Length > 48) title = title[..48] + "…";
                return new ClipboardSnapshot(
                    ClipboardItemKind.Text,
                    capturedAt,
                    title,
                    $"{text.Length} 个字符",
                    text,
                    source,
                    ClipboardContentHash.FromText(text));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // 剪切板可能被另一个进程短暂锁定；下一次序列号变化会继续监听。
        }
        return null;
    }
}
