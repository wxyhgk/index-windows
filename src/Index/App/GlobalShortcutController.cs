using Index.Settings;

namespace Index.App;

/// <summary>
/// Applies persisted shortcut preferences to the platform keyboard hook.
/// Rebuilds all bindings after a preference change, matching the macOS lifecycle.
/// </summary>
public sealed class GlobalShortcutController : IDisposable
{
    private readonly IShortcutSettingsStore _settings;
    private readonly Action _capture;
    private readonly Action _captureVirtualWindow;
    private readonly Action _showGallery;
    private readonly Action _showClipboard;
    private GlobalHotKey? _hotKeys;
    private bool _started;

    public GlobalShortcutController(
        IShortcutSettingsStore settings,
        Action capture,
        Action captureVirtualWindow,
        Action showGallery,
        Action showClipboard)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _captureVirtualWindow = captureVirtualWindow
            ?? throw new ArgumentNullException(nameof(captureVirtualWindow));
        _showGallery = showGallery ?? throw new ArgumentNullException(nameof(showGallery));
        _showClipboard = showClipboard ?? throw new ArgumentNullException(nameof(showClipboard));
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _settings.Changed += OnSettingsChanged;
        Rebuild(_settings.Current);
    }

    private void OnSettingsChanged(object? sender, ShortcutSettings settings) => Rebuild(settings);

    private void Rebuild(ShortcutSettings settings)
    {
        settings.Validate();
        _hotKeys?.Dispose();

        var hotKeys = new GlobalHotKey();
        Register(hotKeys, settings.Capture, _capture);
        Register(hotKeys, settings.VirtualWindow, _captureVirtualWindow);
        Register(hotKeys, settings.Gallery, _showGallery);
        Register(hotKeys, settings.Clipboard, _showClipboard);
        _hotKeys = hotKeys;
    }

    private static void Register(GlobalHotKey hotKeys, KeyboardShortcut shortcut, Action callback)
    {
        var id = hotKeys.Register((uint)shortcut.Modifiers, shortcut.VirtualKey);
        hotKeys.OnHotKey(id, callback);
    }

    public void Dispose()
    {
        if (_started)
            _settings.Changed -= OnSettingsChanged;
        _hotKeys?.Dispose();
        _hotKeys = null;
        _started = false;
    }
}
