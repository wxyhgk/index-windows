namespace Index.Platform.Clipboard;

public interface IClipboardPasteTarget
{
    void RememberForegroundWindow();
    Task PasteAsync(CancellationToken cancellationToken = default);
}
