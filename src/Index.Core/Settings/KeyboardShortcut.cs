namespace Index.Settings;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Control = 0x0002,
    Shift = 0x0004,
    Alt = 0x0008,
    Windows = 0x0010
}

/// <summary>Portable definition of a Windows global shortcut.</summary>
public readonly record struct KeyboardShortcut(uint VirtualKey, HotKeyModifiers Modifiers)
{
    public static KeyboardShortcut CaptureDefault => new(0x41, HotKeyModifiers.Control | HotKeyModifiers.Shift);
    public static KeyboardShortcut GalleryDefault => new(0x47, HotKeyModifiers.Control | HotKeyModifiers.Shift);
    public static KeyboardShortcut ClipboardDefault => new(0x56, HotKeyModifiers.Control | HotKeyModifiers.Shift);

    public bool IsValid => VirtualKey != 0 && Modifiers != HotKeyModifiers.None && !IsModifierKey(VirtualKey);

    public string DisplayString
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotKeyModifiers.Windows)) parts.Add("Win");
            parts.Add(KeyName(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public static bool IsModifierKey(uint virtualKey) => virtualKey is
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static string KeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A || virtualKey is >= 0x30 and <= 0x39)
            return ((char)virtualKey).ToString();

        if (virtualKey is >= 0x70 and <= 0x7B)
            return $"F{virtualKey - 0x6F}";

        return virtualKey switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => $"Key {virtualKey}"
        };
    }
}
