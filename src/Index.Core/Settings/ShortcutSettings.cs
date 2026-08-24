namespace Index.Settings;

public sealed record ShortcutSettings(
    KeyboardShortcut Capture,
    KeyboardShortcut Gallery,
    KeyboardShortcut Clipboard)
{
    public static ShortcutSettings Defaults => new(
        KeyboardShortcut.CaptureDefault,
        KeyboardShortcut.GalleryDefault,
        KeyboardShortcut.ClipboardDefault);

    public void Validate()
    {
        if (!Capture.IsValid)
            throw new ArgumentException("截图快捷键必须包含至少一个修饰键。", nameof(Capture));
        if (!Gallery.IsValid)
            throw new ArgumentException("图库快捷键必须包含至少一个修饰键。", nameof(Gallery));
        if (!Clipboard.IsValid)
            throw new ArgumentException("剪贴板快捷键必须包含至少一个修饰键。", nameof(Clipboard));
        if (Capture == Gallery || Capture == Clipboard || Gallery == Clipboard)
            throw new ArgumentException("截图、图库与剪贴板不能使用相同的快捷键。", nameof(Clipboard));
    }
}
