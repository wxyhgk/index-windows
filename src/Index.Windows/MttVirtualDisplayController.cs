using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Index.Platform;

public readonly record struct VirtualDisplayMode(
    int Width,
    int Height,
    int RefreshRate)
{
    public static VirtualDisplayMode UltraHd60 => new(3840, 2160, 60);

    public void Validate()
    {
        if (Width < 640 || Height < 480 || Width > 7680 || Height > 4320)
            throw new ArgumentOutOfRangeException(nameof(Width), "Unsupported virtual display size.");
        if (RefreshRate is < 24 or > 240)
            throw new ArgumentOutOfRangeException(nameof(RefreshRate));
    }
}

public interface IVirtualDisplayLease : IAsyncDisposable
{
    WindowsDisplayTarget Display { get; }
}

public interface IMttVirtualDisplayController
{
    Task<IVirtualDisplayLease> AcquireAsync(
        VirtualDisplayMode mode,
        CancellationToken cancellationToken = default);
}

public static class VirtualDisplayPlacement
{
    public static (int Left, int Top) FindRightOf(
        IReadOnlyCollection<DisplayBounds> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0)
            return (0, 0);

        return (
            displays.Max(display => display.Right),
            displays.Min(display => display.Top));
    }
}

/// <summary>
/// Temporarily attaches the exact MTT adapter as an extended desktop display. A lease restores
/// its previous mode, position and active state even when capture is canceled or fails.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MttVirtualDisplayController : IMttVirtualDisplayController
{
    private static readonly TimeSpan TopologyTimeout = TimeSpan.FromSeconds(20);
    private readonly IWindowsDisplayCatalog _displayCatalog;
    private readonly WindowsDisplayConfigTopology _topology;
    private readonly VirtualDisplayRecoveryJournal _recoveryJournal;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MttVirtualDisplayController(IWindowsDisplayCatalog displayCatalog)
        : this(
            displayCatalog,
            new WindowsDisplayConfigTopology(),
            new VirtualDisplayRecoveryJournal())
    {
    }

    internal MttVirtualDisplayController(
        IWindowsDisplayCatalog displayCatalog,
        WindowsDisplayConfigTopology topology,
        VirtualDisplayRecoveryJournal recoveryJournal)
    {
        _displayCatalog = displayCatalog
            ?? throw new ArgumentNullException(nameof(displayCatalog));
        _topology = topology ?? throw new ArgumentNullException(nameof(topology));
        _recoveryJournal = recoveryJournal
            ?? throw new ArgumentNullException(nameof(recoveryJournal));
    }

    public async Task<IVirtualDisplayLease> AcquireAsync(
        VirtualDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        mode.Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        DisplayModeState? original = null;
        string? adapterDeviceName = null;
        WindowsDisplayConfigTopology.DisplayTopologySnapshot? topologySnapshot = null;
        try
        {
            await RecoverPendingCaptureAsync(cancellationToken).ConfigureAwait(false);
            var state = _displayCatalog.GetMttVirtualDisplayState();
            adapterDeviceName = state.AdapterDeviceName
                ?? throw new InvalidOperationException(
                    "MTT Virtual Display Driver is not installed.");
            original = ReadCurrentMode(adapterDeviceName);
            var placement = original is { } activeMode
                ? (activeMode.Position.X, activeMode.Position.Y)
                : VirtualDisplayPlacement.FindRightOf(
                    _displayCatalog.GetActiveDisplays()
                        .Select(display => display.Bounds)
                        .ToArray());

            if (original is null)
            {
                _recoveryJournal.MarkPending();
                topologySnapshot = _topology.ActivateMtt();
                var attached = await WaitForStateAsync(
                    _ => true,
                    cancellationToken).ConfigureAwait(false);
                adapterDeviceName = attached.AdapterDeviceName;
            }

            var targetMode = FindMode(adapterDeviceName, mode);
            targetMode.Position = new NativePoint(placement.Item1, placement.Item2);
            ApplyMode(adapterDeviceName, targetMode);
            await WaitForModeAsync(
                adapterDeviceName,
                mode,
                cancellationToken).ConfigureAwait(false);
            var active = await WaitForStateAsync(
                _ => true,
                cancellationToken).ConfigureAwait(false);
            return new Lease(
                this,
                adapterDeviceName,
                original,
                topologySnapshot,
                active);
        }
        catch (Exception captureError)
        {
            try
            {
                await RestoreAsync(
                    adapterDeviceName,
                    original,
                    topologySnapshot,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception restoreError)
            {
                throw new AggregateException(
                    "Virtual display capture failed and its display topology could not be restored.",
                    captureError,
                    restoreError);
            }
            finally
            {
                _gate.Release();
            }
            throw;
        }
    }

    private async Task RestoreAsync(
        string? adapterDeviceName,
        DisplayModeState? original,
        WindowsDisplayConfigTopology.DisplayTopologySnapshot? topologySnapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adapterDeviceName))
            return;

        if (original is { } originalMode)
        {
            ApplyMode(adapterDeviceName, originalMode);
            await WaitForStateAsync(
                display => display.Bounds.Left == originalMode.Position.X
                    && display.Bounds.Top == originalMode.Position.Y
                    && display.Bounds.Width == originalMode.Width
                    && display.Bounds.Height == originalMode.Height,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (topologySnapshot is null)
            return;

        _topology.Restore(topologySnapshot);
        await WaitUntilInactiveAsync(cancellationToken).ConfigureAwait(false);
        _recoveryJournal.Clear();
    }

    private async Task RecoverPendingCaptureAsync(CancellationToken cancellationToken)
    {
        if (!_recoveryJournal.IsPending)
            return;

        if (_displayCatalog.GetMttVirtualDisplayState().ActiveDisplay is not null)
        {
            _topology.DeactivateMtt();
            await WaitUntilInactiveAsync(cancellationToken).ConfigureAwait(false);
        }
        _recoveryJournal.Clear();
    }

    private async Task<WindowsDisplayTarget> WaitForStateAsync(
        Func<WindowsDisplayTarget, bool> predicate,
        CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)TopologyTimeout.TotalMilliseconds;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = _displayCatalog.GetMttVirtualDisplayState().ActiveDisplay;
            if (active is not null && predicate(active))
                return active;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        } while (Environment.TickCount64 < deadline);

        throw new TimeoutException("Windows did not activate the requested MTT display mode.");
    }

    private async Task WaitUntilInactiveAsync(CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)TopologyTimeout.TotalMilliseconds;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_displayCatalog.GetMttVirtualDisplayState().ActiveDisplay is null)
                return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        } while (Environment.TickCount64 < deadline);

        throw new TimeoutException("Windows did not detach the temporary MTT display.");
    }

    private static async Task WaitForModeAsync(
        string adapterDeviceName,
        VirtualDisplayMode requested,
        CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + (long)TopologyTimeout.TotalMilliseconds;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = ReadCurrentMode(adapterDeviceName);
            if (current is not null
                && current.Value.Width == requested.Width
                && current.Value.Height == requested.Height
                && current.Value.Frequency == requested.RefreshRate)
            {
                return;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        } while (Environment.TickCount64 < deadline);

        throw new TimeoutException("Windows did not apply the requested MTT display mode.");
    }

    private static DisplayModeState FindMode(
        string adapterDeviceName,
        VirtualDisplayMode requested)
    {
        for (int modeIndex = 0; modeIndex < 1024; modeIndex++)
        {
            var mode = DisplayModeState.Create();
            if (!EnumDisplaySettingsEx(
                    adapterDeviceName,
                    modeIndex,
                    ref mode,
                    0))
            {
                break;
            }

            if (mode.Width == requested.Width
                && mode.Height == requested.Height
                && mode.Frequency == requested.RefreshRate)
            {
                return mode;
            }
        }

        throw new InvalidOperationException(
            $"MTT VDD does not expose {requested.Width}×{requested.Height}@" +
            $"{requested.RefreshRate} Hz.");
    }

    private static DisplayModeState? ReadCurrentMode(string adapterDeviceName)
    {
        var mode = DisplayModeState.Create();
        return EnumDisplaySettingsEx(
            adapterDeviceName,
            CurrentSettings,
            ref mode,
            0)
            ? mode
            : null;
    }

    private static void ApplyMode(
        string adapterDeviceName,
        DisplayModeState mode)
    {
        mode.Fields = DevModePosition | DevModePelsWidth | DevModePelsHeight;
        if (mode.Width > 0 && mode.Height > 0)
        {
            mode.Fields |= DevModeBitsPerPixel | DevModeDisplayFrequency;
        }
        int applied = ChangeDisplaySettingsEx(
            adapterDeviceName,
            ref mode,
            nint.Zero,
            0,
            nint.Zero);
        ThrowForDisplayChange(applied, "apply");
    }

    private static void ThrowForDisplayChange(int result, string operation)
    {
        if (result == DisplayChangeSuccessful)
            return;
        if (result == DisplayChangeRestart)
        {
            throw new InvalidOperationException(
                $"Windows requires a restart to {operation} the virtual display mode.");
        }

        throw new Win32Exception(
            result,
            $"Windows could not {operation} the virtual display mode (result {result}).");
    }

    private sealed class Lease : IVirtualDisplayLease
    {
        private readonly MttVirtualDisplayController _owner;
        private readonly string _adapterDeviceName;
        private readonly DisplayModeState? _original;
        private readonly WindowsDisplayConfigTopology.DisplayTopologySnapshot? _topologySnapshot;
        private int _disposed;

        public Lease(
            MttVirtualDisplayController owner,
            string adapterDeviceName,
            DisplayModeState? original,
            WindowsDisplayConfigTopology.DisplayTopologySnapshot? topologySnapshot,
            WindowsDisplayTarget display)
        {
            _owner = owner;
            _adapterDeviceName = adapterDeviceName;
            _original = original;
            _topologySnapshot = topologySnapshot;
            Display = display;
        }

        public WindowsDisplayTarget Display { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                await _owner.RestoreAsync(
                    _adapterDeviceName,
                    _original,
                    _topologySnapshot,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _owner._gate.Release();
            }
        }
    }

    private const int CurrentSettings = -1;
    private const uint DevModePosition = 0x00000020;
    private const uint DevModeBitsPerPixel = 0x00040000;
    private const uint DevModePelsWidth = 0x00080000;
    private const uint DevModePelsHeight = 0x00100000;
    private const uint DevModeDisplayFrequency = 0x00400000;
    private const int DisplayChangeSuccessful = 0;
    private const int DisplayChangeRestart = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayModeState
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public NativePoint Position;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPixel;
        public int Width;
        public int Height;
        public uint DisplayFlags;
        public int Frequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;

        public static DisplayModeState Create() => new()
        {
            Size = (ushort)Marshal.SizeOf<DisplayModeState>()
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(
        string deviceName,
        int modeNumber,
        ref DisplayModeState devMode,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ChangeDisplaySettingsEx(
        string? deviceName,
        ref DisplayModeState devMode,
        nint window,
        uint flags,
        nint parameter);

}
