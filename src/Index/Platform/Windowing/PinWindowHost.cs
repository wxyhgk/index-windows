using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Index.Pin;

namespace Index.Platform.Windowing;

/// <summary>Native window operations used by the otherwise WinUI-only pin surface.</summary>
public static class PinWindowHost
{
    private const uint WmSysCommand = 0x0112;
    private const int ScMove = 0xF010;
    private const int HtCaption = 2;

    public static void Configure(Window window, bool alwaysOnTop = true)
    {
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = alwaysOnTop;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        SuppressDwmFrame(window);
    }

    public static void SetAlwaysOnTop(Window window, bool alwaysOnTop)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = alwaysOnTop;
    }

    private static void SuppressDwmFrame(Window window)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == nint.Zero)
            return;

        // Windows 11 can retain a one-pixel activation border and rounded corner pixels even
        // after WinUI removes its title bar. Pins expose the captured pixels edge-to-edge.
        uint noBorder = 0xFFFFFFFE; // DWMWA_COLOR_NONE
        int doNotRound = 1;        // DWMWCP_DONOTROUND
        _ = NativeMethods.DwmSetWindowAttributeColor(hwnd, 34, ref noBorder, sizeof(uint));
        _ = NativeMethods.DwmSetWindowAttributeInt(hwnd, 33, ref doNotRound, sizeof(int));
    }

    public static PinRect WorkAreaAt(double x, double y)
    {
        var display = DisplayArea.GetFromPoint(
            new PointInt32(checked((int)Math.Round(x)), checked((int)Math.Round(y))),
            DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        return new PinRect(area.X, area.Y, area.Width, area.Height);
    }

    public static PinRect CurrentFrame(Window window)
    {
        var position = window.AppWindow.Position;
        var size = window.AppWindow.Size;
        return new PinRect(position.X, position.Y, size.Width, size.Height);
    }

    public static PinRect OffscreenWarmupFrame(PinRect visibleFrame)
    {
        int virtualLeft = NativeMethods.GetSystemMetrics(76); // SM_XVIRTUALSCREEN
        int virtualTop = NativeMethods.GetSystemMetrics(77);  // SM_YVIRTUALSCREEN
        return new PinRect(
            virtualLeft - visibleFrame.Width - 256,
            virtualTop - visibleFrame.Height - 256,
            visibleFrame.Width,
            visibleFrame.Height);
    }

    public static void MoveAndResize(Window window, PinRect frame)
    {
        window.AppWindow.MoveAndResize(new RectInt32(
            checked((int)Math.Round(frame.X)),
            checked((int)Math.Round(frame.Y)),
            Math.Max(48, checked((int)Math.Round(frame.Width))),
            Math.Max(32, checked((int)Math.Round(frame.Height)))));
    }

    public static void BeginMove(Window window)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == nint.Zero)
            return;

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(hwnd, WmSysCommand, ScMove | HtCaption, 0);
    }

    public static double DpiScale(Window window)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        return hwnd == nint.Zero ? 1 : Math.Max(1, NativeMethods.GetDpiForWindow(hwnd)) / 96.0;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(nint hwnd);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        internal static extern int DwmSetWindowAttributeColor(
            nint hwnd,
            int attribute,
            ref uint value,
            int valueSize);

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        internal static extern int DwmSetWindowAttributeInt(
            nint hwnd,
            int attribute,
            ref int value,
            int valueSize);
    }
}
