using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.ComponentModel;

namespace Index.Platform;

/// <summary>
/// 冻结所有显示器画面。骨架阶段使用 GDI BitBlt，
/// 后续替换为 Windows.Graphics.Capture 以获得多屏独立帧。
/// </summary>
public sealed class ScreenFreezer : CaptureSource, IDisplayTopologyProvider
{
    public string Id => "windows.gdi";
    public string Title => "Windows GDI";

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    // GetMonitorInfoW requires MONITORINFOEXW.cbSize (104 bytes on Windows).
    // Without the Unicode charset Marshal.SizeOf reports the ANSI layout and
    // GetMonitorInfoW fails with ERROR_INVALID_PARAMETER (87).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private sealed record MonitorCaptureTarget(IntPtr Handle, DisplayTopologyEntry Display);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hMonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    public DisplayTopologySnapshot GetCurrentTopology()
        => new(EnumAllMonitors().Select(monitor => monitor.Display));

    public async Task<IReadOnlyList<DisplaySnapshot>> MakeSnapshotsAsync()
    {
        var monitors = EnumAllMonitors();
        var selectedMonitor = SelectMonitorAtCursor(monitors);
        var captureOrder = new[] { selectedMonitor }
            .Concat(monitors.Where(monitor => !ReferenceEquals(monitor, selectedMonitor)))
            .ToArray();
        // 在后台线程执行 GDI 捕获，避免阻塞 UI
        var result = await Task.Run(() =>
        {
            var list = new List<DisplaySnapshot>(captureOrder.Length);
            for (int index = 0; index < captureOrder.Length; index++)
            {
                var monitor = captureOrder[index];
                var bounds = monitor.Display.Bounds;
                int width = bounds.Right - bounds.Left;
                int height = bounds.Bottom - bounds.Top;
                var png = CaptureRegion(bounds, width, height);
                list.Add(new DisplaySnapshot
                {
                    DisplayId = monitor.Display.DisplayId,
                    DeviceName = monitor.Display.DeviceName,
                    DisplayIndex = monitors.IndexOf(monitor),
                    IsPrimary = monitor.Display.IsPrimary,
                    Width = width,
                    Height = height,
                    DpiScale = monitor.Display.DpiScale,
                    Left = bounds.Left,
                    Top = bounds.Top,
                    PngData = png
                });
            }
            return list;
        });

        return result;
    }

    private static MonitorCaptureTarget SelectMonitorAtCursor(
        IReadOnlyList<MonitorCaptureTarget> monitors)
    {
        if (monitors.Count == 0)
            throw new InvalidOperationException("Windows did not report an active display.");

        if (!GetCursorPos(out var cursor))
            return monitors[0];

        const uint monitorDefaultToNearest = 2;
        var handle = MonitorFromPoint(cursor, monitorDefaultToNearest);
        return monitors.FirstOrDefault(monitor => monitor.Handle == handle) ?? monitors[0];
    }

    private static List<MonitorCaptureTarget> EnumAllMonitors()
    {
        var list = new List<MonitorCaptureTarget>();
        Exception? enumerationFailure = null;
        bool succeeded = EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hMon, ref info))
                {
                    enumerationFailure = new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Unable to read monitor information.");
                    return false;
                }

                double dpiScale = GetDpiForMonitor(hMon, 0, out uint dpiX, out _) == 0
                    ? dpiX / 96.0
                    : 1.0;
                var identity = ResolveIdentity(info.szDevice);
                list.Add(new MonitorCaptureTarget(
                    hMon,
                    new DisplayTopologyEntry(
                        identity.DisplayId,
                        identity.DeviceName,
                        new DisplayBounds(
                            rect.Left,
                            rect.Top,
                            rect.Right - rect.Left,
                            rect.Bottom - rect.Top),
                        dpiScale,
                        (info.dwFlags & 1) != 0)));
                return true;
            },
            IntPtr.Zero);
        if (enumerationFailure is not null) throw enumerationFailure;
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate displays.");

        // EnumDisplayMonitors does not guarantee order. Keep index zero as primary because the
        // current overlay host treats the first frozen display as its initial screen.
        return list
            .OrderByDescending(monitor => monitor.Display.IsPrimary)
            .ThenBy(monitor => monitor.Display.Bounds.Left)
            .ThenBy(monitor => monitor.Display.Bounds.Top)
            .ThenBy(monitor => monitor.Display.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (string DisplayId, string DeviceName) ResolveIdentity(string adapterDeviceName)
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (EnumDisplayDevices(adapterDeviceName, 0, ref device, 0))
        {
            string hardwareId = string.IsNullOrWhiteSpace(device.DeviceID)
                ? adapterDeviceName
                : device.DeviceID;
            // The Plug and Play device path identifies the monitor independently from the order
            // in which EnumDisplayMonitors happens to return active adapters.
            string stableId = hardwareId.Trim().ToUpperInvariant();
            string friendlyName = string.IsNullOrWhiteSpace(device.DeviceString)
                ? adapterDeviceName
                : device.DeviceString.Trim();
            return (stableId, friendlyName);
        }
        return (adapterDeviceName.Trim().ToUpperInvariant(), adapterDeviceName.Trim());
    }

    private static byte[] CaptureRegion(DisplayBounds bounds, int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(width, height));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
