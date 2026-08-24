using Index.Clipboard;
using Index.Platform.Clipboard;

namespace Index.App;

/// <summary>
/// Decorates only the history popup writer. Other Index clipboard writes remain recordable.
/// </summary>
public sealed class ClipboardHistoryReplayWriter : IClipboardWriter
{
    private readonly IClipboardWriter _inner;
    private readonly ClipboardReplaySuppression _suppression;

    public ClipboardHistoryReplayWriter(
        IClipboardWriter inner,
        ClipboardReplaySuppression suppression)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _suppression = suppression ?? throw new ArgumentNullException(nameof(suppression));
    }

    public void WriteText(string text)
    {
        _inner.WriteText(text);
        _suppression.Mark(ClipboardContentHash.FromText(text));
    }

    public async ValueTask WritePngAsync(
        ReadOnlyMemory<byte> pngData,
        CancellationToken cancellationToken = default)
    {
        await _inner.WritePngAsync(pngData, cancellationToken);
        _suppression.Mark(ClipboardContentHash.FromImagePng(pngData.Span));
    }

    public async ValueTask WriteFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        await _inner.WriteFilesAsync(paths, cancellationToken);
        _suppression.Mark(ClipboardContentHash.FromFiles(paths));
    }
}
