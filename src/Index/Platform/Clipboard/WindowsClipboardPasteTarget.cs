using System.Runtime.InteropServices;

namespace Index.Platform.Clipboard;

/// <summary>Restores the app that owned focus before the popup and sends Ctrl+V.</summary>
public sealed class WindowsClipboardPasteTarget : IClipboardPasteTarget
{
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;
    private const int SwRestore = 9;

    private nint _previousWindow;

    public void RememberForegroundWindow()
    {
        nint hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == nint.Zero)
        {
            _previousWindow = nint.Zero;
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        _previousWindow = processId == (uint)Environment.ProcessId ? nint.Zero : hwnd;
    }

    public async Task PasteAsync(CancellationToken cancellationToken = default)
    {
        nint hwnd = _previousWindow;
        _previousWindow = nint.Zero;
        if (hwnd == nint.Zero || !NativeMethods.IsWindow(hwnd)) return;

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        RestoreForegroundWindow(hwnd);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        if (NativeMethods.GetForegroundWindow() != hwnd)
        {
            RestoreForegroundWindow(hwnd);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        NativeMethods.keybd_event(VkControl, 0, 0, nuint.Zero);
        NativeMethods.keybd_event(VkV, 0, 0, nuint.Zero);
        NativeMethods.keybd_event(VkV, 0, KeyEventKeyUp, nuint.Zero);
        NativeMethods.keybd_event(VkControl, 0, KeyEventKeyUp, nuint.Zero);
    }

    private static void RestoreForegroundWindow(nint hwnd)
    {
        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        nint currentForeground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = currentForeground == nint.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(currentForeground, out _);

        bool attachedTarget = targetThread != 0
            && targetThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, targetThread, true);
        bool attachedForeground = foregroundThread != 0
            && foregroundThread != currentThread
            && foregroundThread != targetThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = NativeMethods.ShowWindowAsync(hwnd, SwRestore);
            _ = NativeMethods.BringWindowToTop(hwnd);
            _ = NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedForeground)
                _ = NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedTarget)
                _ = NativeMethods.AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint threadId, uint attachToThreadId, bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(nint hwnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BringWindowToTop(nint hwnd);

        [DllImport("user32.dll")]
        internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);
    }
}
