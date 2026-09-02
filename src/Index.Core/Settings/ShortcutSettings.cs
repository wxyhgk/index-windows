namespace Index.Settings;

public sealed record ShortcutSettings(
    KeyboardShortcut Capture,
    KeyboardShortcut Gallery,
    KeyboardShortcut Clipboard,
    KeyboardShortcut VirtualWindow,
    bool Automatic4KCapture = false)
{
    public static ShortcutSettings Defaults => new(
        KeyboardShortcut.CaptureDefault,
        KeyboardShortcut.GalleryDefault,
        KeyboardShortcut.ClipboardDefault,
        KeyboardShortcut.VirtualWindowDefault,
        Automatic4KCapture: false);

    public void Validate()
    {
        if (!Capture.IsValid)
            throw new ArgumentException("截图快捷键必须包含至少一个修饰键。", nameof(Capture));
        if (!Gallery.IsValid)
            throw new ArgumentException("图库快捷键必须包含至少一个修饰键。", nameof(Gallery));
        if (!Clipboard.IsValid)
            throw new ArgumentException("剪贴板快捷键必须包含至少一个修饰键。", nameof(Clipboard));
        if (!VirtualWindow.IsValid)
            throw new ArgumentException("4K 当前窗口快捷键必须包含至少一个修饰键。", nameof(VirtualWindow));

        KeyboardShortcut[] shortcuts = [Capture, Gallery, Clipboard, VirtualWindow];
        if (shortcuts.Distinct().Count() != shortcuts.Length)
            throw new ArgumentException("截图、4K 当前窗口、图库与剪贴板不能使用相同的快捷键。", nameof(VirtualWindow));
    }
}
