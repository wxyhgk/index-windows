using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Index.Platform;

public sealed record WindowsDisplayTarget(
    nint MonitorHandle,
    string AdapterDeviceName,
    string DisplayId,
    string DeviceName,
    DisplayBounds Bounds,
    double DpiScale,
    bool IsPrimary);

public enum MttVirtualDisplayAvailability
{
    NotInstalled,
    InstalledInactive,
    Active
}

public sealed record MttVirtualDisplayState(
    MttVirtualDisplayAvailability Availability,
    WindowsDisplayTarget? ActiveDisplay,
    string? AdapterDeviceName = null);

public interface IWindowsDisplayCatalog
{
    IReadOnlyList<WindowsDisplayTarget> GetActiveDisplays();
    MttVirtualDisplayState GetMttVirtualDisplayState();
}

/// <summary>Pure identity policy kept separate from live Win32 enumeration for unit testing.</summary>
public static class MttVirtualDisplayIdentity
{
    public const string MonitorHardwareCode = "MTT1337";

    public static bool IsMatch(string? displayId, string? deviceName)
    {
        return Contains(displayId, MonitorHardwareCode)
            || Contains(deviceName, "VDD by MTT");
    }

    private static bool Contains(string? value, string marker) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(marker, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Enumerates active Windows desktop monitors and preserves the monitor PnP identity. The MTT
/// install probe deliberately uses its EDID hardware code rather than a generic "virtual"
/// friendly name because several unrelated virtual display adapters can coexist.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayCatalog : IWindowsDisplayCatalog
{
    private const string MttMonitorEnumKey =
        @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + MttVirtualDisplayIdentity.MonitorHardwareCode;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(
        nint monitor,
        nint monitorDc,
        ref Rect bounds,
        nint data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint dc,
        nint clip,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfoEx monitorInfo);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    public IReadOnlyList<WindowsDisplayTarget> GetActiveDisplays()
    {
        var displays = new List<WindowsDisplayTarget>();
        Exception? enumerationFailure = null;
        bool succeeded = EnumDisplayMonitors(
            nint.Zero,
            nint.Zero,
            (nint monitor, nint _, ref Rect bounds, nint _) =>
            {
                var info = new MonitorInfoEx
                {
                    Size = Marshal.SizeOf<MonitorInfoEx>()
                };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    enumerationFailure = new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Unable to read monitor information.");
                    return false;
                }

                var identity = ResolveIdentity(info.DeviceName);
                double dpiScale = GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0
                    ? dpiX / 96.0
                    : 1.0;
                displays.Add(new WindowsDisplayTarget(
                    monitor,
                    info.DeviceName,
                    identity.DisplayId,
                    identity.DeviceName,
                    new DisplayBounds(
                        bounds.Left,
                        bounds.Top,
                        bounds.Right - bounds.Left,
                        bounds.Bottom - bounds.Top),
                    dpiScale,
                    (info.Flags & 1) != 0));
                return true;
            },
            nint.Zero);

        if (enumerationFailure is not null)
            throw enumerationFailure;
        if (!succeeded)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to enumerate active displays.");
        }

        return displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.Bounds.Left)
            .ThenBy(display => display.Bounds.Top)
            .ThenBy(display => display.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public MttVirtualDisplayState GetMttVirtualDisplayState()
    {
        var active = GetActiveDisplays()
            .Where(display => MttVirtualDisplayIdentity.IsMatch(
                display.DisplayId,
                display.DeviceName))
            .OrderBy(display => display.DisplayId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (active is not null)
        {
            return new MttVirtualDisplayState(
                MttVirtualDisplayAvailability.Active,
                active,
                active.AdapterDeviceName);
        }

        string? adapterDeviceName = FindMttAdapterDeviceName();
        return new MttVirtualDisplayState(
            adapterDeviceName is not null || IsMttDriverInstalled()
                ? MttVirtualDisplayAvailability.InstalledInactive
                : MttVirtualDisplayAvailability.NotInstalled,
            null,
            adapterDeviceName);
    }

    private static string? FindMttAdapterDeviceName()
    {
        const uint getDeviceInterfaceName = 1;
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
                return null;

            bool adapterIdentityMatches =
                adapter.DeviceId.Contains("MTTVDD", StringComparison.OrdinalIgnoreCase);
            for (uint monitorIndex = 0; ; monitorIndex++)
            {
                var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(
                        adapter.DeviceName,
                        monitorIndex,
                        ref monitor,
                        getDeviceInterfaceName))
                {
                    break;
                }

                if (MttVirtualDisplayIdentity.IsMatch(
                        monitor.DeviceId,
                        monitor.DeviceString))
                {
                    return adapter.DeviceName;
                }
            }

            if (adapterIdentityMatches)
                return adapter.DeviceName;
        }
    }

    private static bool IsMttDriverInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MttMonitorEnumKey, writable: false);
            return key is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (string DisplayId, string DeviceName) ResolveIdentity(
        string adapterDeviceName)
    {
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        if (EnumDisplayDevices(adapterDeviceName, 0, ref device, 0))
        {
            string hardwareId = string.IsNullOrWhiteSpace(device.DeviceId)
                ? adapterDeviceName
                : device.DeviceId;
            string friendlyName = string.IsNullOrWhiteSpace(device.DeviceString)
                ? adapterDeviceName
                : device.DeviceString.Trim();
            return (hardwareId.Trim().ToUpperInvariant(), friendlyName);
        }

        string fallback = adapterDeviceName.Trim();
        return (fallback.ToUpperInvariant(), fallback);
    }
}
