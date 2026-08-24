namespace Index.Platform.Clipboard;

/// <summary>Small PAL seam so clipboard side effects never leak into the popup view model.</summary>
public interface IClipboardWriter
{
    void WriteText(string text);

    ValueTask WritePngAsync(
        ReadOnlyMemory<byte> pngData,
        CancellationToken cancellationToken = default);

    ValueTask WriteFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
}
