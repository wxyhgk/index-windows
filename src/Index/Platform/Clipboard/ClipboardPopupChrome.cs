using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Index.Platform.Clipboard;

/// <summary>Windows-only chrome for the clipboard floating panel.</summary>
public static class ClipboardPopupChrome
{
    public static void ApplyStandard(Window window)
    {
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(true, true);
    }

    public static bool IsForegroundOrCapturing(Window window)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == nint.Zero) return false;
        return NativeMethods.GetForegroundWindow() == hwnd
            || NativeMethods.GetCapture() == hwnd;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern nint GetCapture();


    }
}
