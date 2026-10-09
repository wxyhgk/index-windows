using Index.Settings;

namespace Index.Tests;

public sealed class ShortcutSettingsTests
{
    [Fact]
    public void Defaults_AreDistinctAndValid()
    {
        var settings = ShortcutSettings.Defaults;

        settings.Validate();
        Assert.NotEqual(settings.Capture, settings.Gallery);
        Assert.NotEqual(settings.Gallery, settings.Clipboard);
        Assert.NotEqual(settings.Capture, settings.VirtualWindow);
        Assert.Equal("Ctrl + Shift + A", settings.Capture.DisplayString);
        Assert.Equal("Ctrl + Shift + 4", settings.VirtualWindow.DisplayString);
        Assert.Equal("Ctrl + Shift + G", settings.Gallery.DisplayString);
        Assert.Equal("Ctrl + Shift + V", settings.Clipboard.DisplayString);
    }

    [Fact]
    public void Save_RoundTripsAndRaisesChanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Index.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new ShortcutSettingsStore(path);
            ShortcutSettings? notification = null;
            store.Changed += (_, settings) => notification = settings;
            var expected = new ShortcutSettings(
                new KeyboardShortcut(0x53, HotKeyModifiers.Control | HotKeyModifiers.Alt),
                new KeyboardShortcut(0x4C, HotKeyModifiers.Control | HotKeyModifiers.Shift),
                new KeyboardShortcut(0x56, HotKeyModifiers.Control | HotKeyModifiers.Alt),
                new KeyboardShortcut(0x34, HotKeyModifiers.Control | HotKeyModifiers.Alt),
                Automatic4KCapture: true,
                DelayedCapture: new KeyboardShortcut(0x44, HotKeyModifiers.Control | HotKeyModifiers.Shift));

            store.Save(expected);

            Assert.Equal(expected, notification);
            Assert.Equal(expected, new ShortcutSettingsStore(path).Current);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidOrDuplicateBindings_AreRejected()
    {
        var noModifier = new KeyboardShortcut(0x41, HotKeyModifiers.None);
        Assert.Throws<ArgumentException>(() =>
            new ShortcutSettings(
                noModifier,
                KeyboardShortcut.GalleryDefault,
                KeyboardShortcut.ClipboardDefault,
                KeyboardShortcut.VirtualWindowDefault,
                DelayedCapture: KeyboardShortcut.DelayedCaptureDefault).Validate());

        var duplicate = KeyboardShortcut.CaptureDefault;
        Assert.Throws<ArgumentException>(() =>
            new ShortcutSettings(
                duplicate,
                duplicate,
                KeyboardShortcut.ClipboardDefault,
                KeyboardShortcut.VirtualWindowDefault,
                DelayedCapture: KeyboardShortcut.DelayedCaptureDefault).Validate());
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Index.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "{ definitely not json }");

            Assert.Equal(ShortcutSettings.Defaults, new ShortcutSettingsStore(path).Current);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacyTwoShortcutFileAddsClipboardDefaultWithoutLosingCustomBindings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Index.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """
                {
                  "capture": { "virtualKey": 83, "modifiers": 6 },
                  "gallery": { "virtualKey": 76, "modifiers": 6 }
                }
                """);

            var loaded = new ShortcutSettingsStore(path).Current;

            Assert.Equal(0x53u, loaded.Capture.VirtualKey);
            Assert.Equal(0x4Cu, loaded.Gallery.VirtualKey);
            Assert.Equal(KeyboardShortcut.ClipboardDefault, loaded.Clipboard);
            Assert.Equal(KeyboardShortcut.VirtualWindowDefault, loaded.VirtualWindow);
            Assert.False(loaded.Automatic4KCapture);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacyThreeShortcutFileAddsVirtualWindowDefault()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Index.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """
                {
                  "capture": { "virtualKey": 83, "modifiers": 6 },
                  "gallery": { "virtualKey": 76, "modifiers": 6 },
                  "clipboard": { "virtualKey": 86, "modifiers": 10 }
                }
                """);

            var loaded = new ShortcutSettingsStore(path).Current;

            Assert.Equal(0x53u, loaded.Capture.VirtualKey);
            Assert.Equal(0x4Cu, loaded.Gallery.VirtualKey);
            Assert.Equal(0x56u, loaded.Clipboard.VirtualKey);
            Assert.Equal(KeyboardShortcut.VirtualWindowDefault, loaded.VirtualWindow);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
