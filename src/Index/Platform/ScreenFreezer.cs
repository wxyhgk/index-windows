using System.Runtime.InteropServices;

namespace Index.Platform;

/// <summary>
/// 通过 Windows Graphics Capture 冻结所有显示器画面。
/// </summary>
public sealed class ScreenFreezer : CaptureSource, IDisplayTopologyProvider
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(3);
    private readonly IDisplaySurfaceCapture _displayCapture;
    private readonly IWindowsDisplayCatalog _displayCatalog;

    public ScreenFreezer(
        IDisplaySurfaceCapture displayCapture,
        IWindowsDisplayCatalog displayCatalog)
    {
        _displayCapture = displayCapture
            ?? throw new ArgumentNullException(nameof(displayCapture));
        _displayCatalog = displayCatalog
            ?? throw new ArgumentNullException(nameof(displayCatalog));
    }

    public string Id => "windows.graphics-capture";
    public string Title => "Windows Graphics Capture";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    public DisplayTopologySnapshot GetCurrentTopology()
        => new(EnumAllMonitors().Select(ToTopologyEntry));

    public async Task<IReadOnlyList<DisplaySnapshot>> MakeSnapshotsAsync()
    {
        var monitors = EnumAllMonitors();
        var selectedMonitor = SelectMonitorAtCursor(monitors);
        var captureOrder = new[] { selectedMonitor }
            .Concat(monitors.Where(monitor => !ReferenceEquals(monitor, selectedMonitor)))
            .ToArray();
        // Start every monitor capture before awaiting any one of them so the frozen frames are
        // as close together in time as WGC permits.
        var captures = captureOrder.Select(async monitor =>
        {
            var bounds = monitor.Bounds;
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            var png = await _displayCapture.TryCaptureMonitorPngAsync(
                monitor.MonitorHandle,
                CaptureTimeout).ConfigureAwait(false);
            if (png is null)
            {
                throw new InvalidOperationException(
                    $"Windows Graphics Capture could not capture display " +
                    $"'{monitor.DeviceName}'. GDI fallback is disabled.");
            }

            return new DisplaySnapshot
            {
                DisplayId = monitor.DisplayId,
                DeviceName = monitor.DeviceName,
                DisplayIndex = Array.IndexOf(monitors, monitor),
                IsPrimary = monitor.IsPrimary,
                Width = width,
                Height = height,
                DpiScale = monitor.DpiScale,
                Left = bounds.Left,
                Top = bounds.Top,
                PngData = png
            };
        }).ToArray();

        return await Task.WhenAll(captures).ConfigureAwait(false);
    }

    private static WindowsDisplayTarget SelectMonitorAtCursor(
        IReadOnlyList<WindowsDisplayTarget> monitors)
    {
        if (monitors.Count == 0)
            throw new InvalidOperationException("Windows did not report an active display.");

        if (!GetCursorPos(out var cursor))
            return monitors[0];

        const uint monitorDefaultToNearest = 2;
        var handle = MonitorFromPoint(cursor, monitorDefaultToNearest);
        return monitors.FirstOrDefault(monitor => monitor.MonitorHandle == handle) ?? monitors[0];
    }

    private WindowsDisplayTarget[] EnumAllMonitors()
        => _displayCatalog.GetActiveDisplays().ToArray();

    private static DisplayTopologyEntry ToTopologyEntry(WindowsDisplayTarget display) => new(
        display.DisplayId,
        display.DeviceName,
        display.Bounds,
        display.DpiScale,
        display.IsPrimary);

}
