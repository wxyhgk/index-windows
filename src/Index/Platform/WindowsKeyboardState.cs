using System.Runtime.InteropServices;
using Index.Settings;

namespace Index.Platform;

public static class WindowsKeyboardState
{
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    public static HotKeyModifiers CurrentModifiers()
    {
        var modifiers = HotKeyModifiers.None;
        if (IsDown(0x11)) modifiers |= HotKeyModifiers.Control;
        if (IsDown(0x10)) modifiers |= HotKeyModifiers.Shift;
        if (IsDown(0x12)) modifiers |= HotKeyModifiers.Alt;
        if (IsDown(0x5B) || IsDown(0x5C)) modifiers |= HotKeyModifiers.Windows;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;
}
