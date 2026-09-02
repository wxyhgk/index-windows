using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Index.Platform;

internal readonly record struct DisplayConfigTargetIdentity(
    string MonitorDevicePath,
    string FriendlyName,
    bool IsActive,
    bool IsAvailable,
    uint SourceId,
    uint TargetId);

internal static class MttDisplayConfigPathPolicy
{
    public static int SelectActivationTarget(
        IReadOnlyList<DisplayConfigTargetIdentity> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var matches = targets
            .Select((target, index) => (target, index))
            .Where(candidate => candidate.target.IsAvailable
                && MttVirtualDisplayIdentity.IsMatch(
                    candidate.target.MonitorDevicePath,
                    candidate.target.FriendlyName))
            .OrderByDescending(candidate => candidate.target.IsActive)
            .ThenBy(candidate => candidate.target.SourceId)
            .ThenBy(candidate => candidate.target.TargetId)
            .ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                "The installed MTT display does not expose an available DisplayConfig path.");
        }

        return matches[0].index;
    }
}

/// <summary>
/// Adds or removes only the MTT target path while preserving every currently active physical
/// path. This deliberately never uses a broad topology preset such as SDC_TOPOLOGY_EXTEND.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDisplayConfigTopology
{
    public DisplayTopologySnapshot ActivateMtt()
    {
        var configuration = QueryAll();
        var original = new DisplayTopologySnapshot(
            configuration.Paths.Where(IsActive).ToArray(),
            configuration.Modes);
        var identities = configuration.Paths
            .Select(CreateIdentity)
            .ToArray();
        int targetIndex = MttDisplayConfigPathPolicy.SelectActivationTarget(identities);
        if (identities[targetIndex].IsActive)
            return original;
        if (original.Paths.Length == 0)
        {
            throw new InvalidOperationException(
                "Windows has no active display path to preserve while MTT is temporary.");
        }

        var mttPath = configuration.Paths[targetIndex];
        mttPath.Flags |= DisplayConfigPathActive;
        mttPath.Source.ModeInfoIndex = DisplayConfigModeInfoInvalid;
        mttPath.Target.ModeInfoIndex = DisplayConfigModeInfoInvalid;
        Apply(original.Paths.Append(mttPath).ToArray(), configuration.Modes);
        return original;
    }

    public void Restore(DisplayTopologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Apply(snapshot.Paths, snapshot.Modes);
    }

    public void DeactivateMtt()
    {
        var configuration = QueryAll();
        var remaining = configuration.Paths
            .Where(path => IsActive(path) && !IsMtt(path))
            .ToArray();
        bool hadActiveMtt = configuration.Paths.Any(
            path => IsActive(path) && IsMtt(path));
        if (!hadActiveMtt)
            return;
        if (remaining.Length == 0)
        {
            throw new InvalidOperationException(
                "The temporary MTT display is the only active path; automatic recovery " +
                "would leave Windows without a display.");
        }

        Apply(remaining, configuration.Modes);
    }

    private static DisplayConfigTargetIdentity CreateIdentity(DisplayConfigPathInfo path)
    {
        var request = DisplayConfigTargetDeviceName.Create(
            path.Target.AdapterId,
            path.Target.Id);
        int result = DisplayConfigGetDeviceInfo(ref request);
        if (result != 0)
        {
            return new DisplayConfigTargetIdentity(
                string.Empty,
                string.Empty,
                IsActive(path),
                path.Target.TargetAvailable,
                path.Source.Id,
                path.Target.Id);
        }

        return new DisplayConfigTargetIdentity(
            request.MonitorDevicePath ?? string.Empty,
            request.MonitorFriendlyDeviceName ?? string.Empty,
            IsActive(path),
            path.Target.TargetAvailable,
            path.Source.Id,
            path.Target.Id);
    }

    private static bool IsActive(DisplayConfigPathInfo path) =>
        (path.Flags & DisplayConfigPathActive) != 0;

    private static bool IsMtt(DisplayConfigPathInfo path)
    {
        var identity = CreateIdentity(path);
        return MttVirtualDisplayIdentity.IsMatch(
            identity.MonitorDevicePath,
            identity.FriendlyName);
    }

    private static DisplayConfiguration QueryAll()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(
                QueryAllPaths,
                out uint pathCount,
                out uint modeCount);
            ThrowForWin32Result(result, "get display configuration buffer sizes");

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            result = QueryDisplayConfig(
                QueryAllPaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                nint.Zero);
            if (result == ErrorInsufficientBuffer)
                continue;
            ThrowForWin32Result(result, "query display configuration");

            return new DisplayConfiguration(
                paths.Take(checked((int)pathCount)).ToArray(),
                modes.Take(checked((int)modeCount)).ToArray());
        }

        throw new InvalidOperationException(
            "The Windows display topology changed repeatedly while it was being read.");
    }

    private static void Apply(
        DisplayConfigPathInfo[] paths,
        DisplayConfigModeInfo[] modes)
    {
        const uint commonFlags = UseSuppliedDisplayConfig | AllowChanges;
        int validation = SetDisplayConfig(
            checked((uint)paths.Length),
            paths,
            checked((uint)modes.Length),
            modes,
            commonFlags | ValidateConfiguration);
        ThrowForWin32Result(validation, "validate the targeted display configuration");

        int applied = SetDisplayConfig(
            checked((uint)paths.Length),
            paths,
            checked((uint)modes.Length),
            modes,
            commonFlags | ApplyConfiguration);
        ThrowForWin32Result(applied, "apply the targeted display configuration");
    }

    private static void ThrowForWin32Result(int result, string operation)
    {
        if (result != 0)
            throw new Win32Exception(result, $"Windows could not {operation} (result {result}).");
    }

    internal sealed record DisplayTopologySnapshot(
        DisplayConfigPathInfo[] Paths,
        DisplayConfigModeInfo[] Modes);

    private sealed record DisplayConfiguration(
        DisplayConfigPathInfo[] Paths,
        DisplayConfigModeInfo[] Modes);

    private const int ErrorInsufficientBuffer = 122;
    private const uint QueryAllPaths = 0x00000001;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint DisplayConfigModeInfoInvalid = 0xFFFFFFFF;
    private const uint UseSuppliedDisplayConfig = 0x00000020;
    private const uint ValidateConfiguration = 0x00000040;
    private const uint ApplyConfiguration = 0x00000080;
    private const uint AllowChanges = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LocallyUniqueIdentifier
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        public LocallyUniqueIdentifier AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        public LocallyUniqueIdentifier AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo Source;
        public DisplayConfigPathTargetInfo Target;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct DisplayConfigModeInfo
    {
        [FieldOffset(0)] public uint InfoType;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public LocallyUniqueIdentifier AdapterId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public uint Type;
        public uint Size;
        public LocallyUniqueIdentifier AdapterId;
        public uint Id;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;

        public static DisplayConfigTargetDeviceName Create(
            LocallyUniqueIdentifier adapterId,
            uint id) => new()
            {
                Type = 2,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = adapterId,
                Id = id
            };
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint pathCount,
        out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(
        ref DisplayConfigTargetDeviceName request);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint pathCount,
        [In] DisplayConfigPathInfo[] paths,
        uint modeCount,
        [In] DisplayConfigModeInfo[] modes,
        uint flags);
}
