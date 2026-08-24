using Index.Platform;
using Index.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Index.UI.Settings;

/// <summary>Small keyboard recorder used by the shortcut settings page.</summary>
internal sealed class ShortcutRecorderControl : Button
{
    private KeyboardShortcut _shortcut;
    private bool _recording;

    public ShortcutRecorderControl(KeyboardShortcut shortcut)
    {
        MinWidth = 150;
        HorizontalAlignment = HorizontalAlignment.Right;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        CornerRadius = new CornerRadius(6);
        SetShortcut(shortcut);
        Click += (_, _) => BeginRecording();
        KeyDown += OnKeyDown;
        LostFocus += (_, _) => CancelRecording();
    }

    public event EventHandler<KeyboardShortcut>? ShortcutRecorded;

    public void SetShortcut(KeyboardShortcut shortcut)
    {
        _shortcut = shortcut;
        if (!_recording)
            Content = shortcut.DisplayString;
    }

    private void BeginRecording()
    {
        _recording = true;
        Content = "请按新的组合键…";
        Focus(FocusState.Programmatic);
    }

    private void CancelRecording()
    {
        _recording = false;
        Content = _shortcut.DisplayString;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_recording)
            return;

        var virtualKey = (uint)args.Key;
        args.Handled = true;
        if (virtualKey == 0x1B)
        {
            CancelRecording();
            return;
        }

        if (KeyboardShortcut.IsModifierKey(virtualKey))
            return;

        var candidate = new KeyboardShortcut(virtualKey, WindowsKeyboardState.CurrentModifiers());
        if (!candidate.IsValid)
        {
            Content = "请同时按 Ctrl / Alt / Shift / Win";
            return;
        }

        _shortcut = candidate;
        _recording = false;
        Content = candidate.DisplayString;
        ShortcutRecorded?.Invoke(this, candidate);
    }
}
