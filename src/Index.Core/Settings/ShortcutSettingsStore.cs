using System.Text.Json;

namespace Index.Settings;

public interface IShortcutSettingsStore
{
    ShortcutSettings Current { get; }
    event EventHandler<ShortcutSettings>? Changed;
    void Save(ShortcutSettings settings);
    void Reset();
}

/// <summary>Persists shortcut preferences separately from the screenshot database.</summary>
public sealed class ShortcutSettingsStore : IShortcutSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _path;
    private ShortcutSettings _current;

    public ShortcutSettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _current = Load(_path);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Index",
        "settings.json");

    public ShortcutSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public event EventHandler<ShortcutSettings>? Changed;

    public void Save(ShortcutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        lock (_gate)
        {
            if (settings == _current)
                return;

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            _current = settings;
        }

        Changed?.Invoke(this, settings);
    }

    public void Reset() => Save(ShortcutSettings.Defaults);

    private static ShortcutSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return ShortcutSettings.Defaults;

            var settings = JsonSerializer.Deserialize<ShortcutSettings>(File.ReadAllText(path), JsonOptions);
            if (settings is not null && !settings.Clipboard.IsValid)
                settings = settings with { Clipboard = KeyboardShortcut.ClipboardDefault };
            if (settings is not null && !settings.VirtualWindow.IsValid)
                settings = settings with { VirtualWindow = KeyboardShortcut.VirtualWindowDefault };
            if (settings is not null && !settings.DelayedCapture.IsValid)
                settings = settings with { DelayedCapture = KeyboardShortcut.DelayedCaptureDefault };
            settings?.Validate();
            return settings ?? ShortcutSettings.Defaults;
        }
        catch (JsonException)
        {
            return ShortcutSettings.Defaults;
        }
        catch (ArgumentException)
        {
            return ShortcutSettings.Defaults;
        }
    }
}
