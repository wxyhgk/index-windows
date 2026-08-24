using System.Runtime.InteropServices;

namespace Index.App;

/// <summary>
/// 全局热键：用 WH_KEYBOARD_LL 钩子拦截键盘事件，不依赖窗口消息循环。
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private sealed class Binding
    {
        public int Vk;
        public uint Modifiers;
        public Action? Callback;
    }

    private nint _hookId;
    private readonly LowLevelKeyboardProc _proc;
    private readonly List<Binding> _bindings = new();
    private readonly HashSet<int> _pressedKeys = new();

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint idHook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint idHook, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? lpModuleName);

    public GlobalHotKey()
    {
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hookId == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// 注册热键。modifiers: MOD_CONTROL(2)|MOD_SHIFT(4)|MOD_ALT(8)|MOD_WIN(16)
    /// </summary>
    public int Register(uint modifiers, uint vk)
    {
        int id = _bindings.Count;
        _bindings.Add(new Binding { Vk = (int)vk, Modifiers = modifiers, Callback = null });
        return id;
    }

    public void OnHotKey(int id, Action callback)
    {
        _bindings[id].Callback = callback;
    }

    private static bool CheckModifiers(uint modifiers)
    {
        if ((modifiers & 0x0002) != 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0) return false; // Ctrl
        if ((modifiers & 0x0004) != 0 && (GetAsyncKeyState(0x10) & 0x8000) == 0) return false; // Shift
        if ((modifiers & 0x0008) != 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0) return false; // Alt
        if ((modifiers & 0x0010) != 0 && (GetAsyncKeyState(0x5B) & 0x8000) == 0) return false; // Win
        return true;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            int message = unchecked((int)wParam);
            var keyData = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vkCode = unchecked((int)keyData.VkCode);

            if (message == WM_KEYUP || message == WM_SYSKEYUP)
            {
                _pressedKeys.Remove(vkCode);
                if (IsModifierKey(vkCode))
                    _pressedKeys.Clear();
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (message != WM_KEYDOWN && message != WM_SYSKEYDOWN)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            bool isFirstPress = _pressedKeys.Add(vkCode);
            foreach (var b in _bindings)
            {
                if (b.Vk == vkCode && b.Callback != null && CheckModifiers(b.Modifiers))
                {
                    if (isFirstPress)
                        b.Callback();
                    return 1;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(int virtualKey) => virtualKey is
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public void Dispose()
    {
        if (_hookId != 0)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = 0;
        }
    }
}
