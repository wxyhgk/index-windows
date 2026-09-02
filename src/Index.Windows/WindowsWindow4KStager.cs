using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Index.Platform;

public interface IStagedWindowLease : IAsyncDisposable
{
    nint WindowHandle { get; }
    SourceWindowBounds OriginalBounds { get; }
    SourceWindowBounds StagedBounds { get; }
}

public interface IWindow4KStagingRequest
{
    nint WindowHandle { get; }
    uint ProcessId { get; }
    SourceWindowBounds OriginalBounds { get; }
}

public interface IWindow4KStager
{
    IWindow4KStagingRequest Prepare(nint windowHandle);

    Task<IStagedWindowLease> StageAsync(
        IWindow4KStagingRequest request,
        DisplayBounds targetBounds,
        double targetDpiScale,
        CancellationToken cancellationToken = default);
}

public static class WindowDensityPlacement
{
    public static SourceWindowBounds CalculateVisibleBounds(
        SourceWindowBounds originalVisibleBounds,
        DisplayBounds sourceDisplayBounds,
        double sourceDpiScale,
        DisplayBounds targetDisplayBounds,
        double targetDpiScale)
    {
        if (originalVisibleBounds.Width <= 0 || originalVisibleBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(originalVisibleBounds));
        if (sourceDisplayBounds.Width <= 0 || sourceDisplayBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDisplayBounds));
        if (targetDisplayBounds.Width <= 0 || targetDisplayBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetDisplayBounds));
        if (!double.IsFinite(sourceDpiScale) || sourceDpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDpiScale));
        if (!double.IsFinite(targetDpiScale) || targetDpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetDpiScale));

        double densityRatio = targetDpiScale / sourceDpiScale;
        int width = checked((int)Math.Round(originalVisibleBounds.Width * densityRatio));
        int height = checked((int)Math.Round(originalVisibleBounds.Height * densityRatio));
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The density-preserving window size is invalid.");

        int mappedLeft = checked(targetDisplayBounds.Left + (int)Math.Round(
            (originalVisibleBounds.Left - sourceDisplayBounds.Left) * densityRatio));
        int mappedTop = checked(targetDisplayBounds.Top + (int)Math.Round(
            (originalVisibleBounds.Top - sourceDisplayBounds.Top) * densityRatio));
        // WGC captures the HWND's compositor surface, not just the monitor intersection. A 16:10
        // source window can therefore remain a true 2x size (for example 3840x2304) even though
        // the 16:9 MTT monitor is only 3840x2160. Anchor an oversized axis at the target origin;
        // shrinking it to fit would change the app's logical layout and defeat density capture.
        int left = width <= targetDisplayBounds.Width
            ? Math.Clamp(
                mappedLeft,
                targetDisplayBounds.Left,
                checked(targetDisplayBounds.Right - width))
            : targetDisplayBounds.Left;
        int top = height <= targetDisplayBounds.Height
            ? Math.Clamp(
                mappedTop,
                targetDisplayBounds.Top,
                checked(targetDisplayBounds.Bottom - height))
            : targetDisplayBounds.Top;
        return new SourceWindowBounds(left, top, checked(left + width), checked(top + height));
    }
}

public static class WindowFramePlacement
{
    public static SourceWindowBounds CalculateRawBounds(
        SourceWindowBounds targetVisibleBounds,
        SourceWindowBounds currentRawBounds,
        SourceWindowBounds currentVisibleBounds)
    {
        if (targetVisibleBounds.Width <= 0 || targetVisibleBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetVisibleBounds));
        if (currentRawBounds.Width <= 0 || currentRawBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentRawBounds));
        if (currentVisibleBounds.Width <= 0 || currentVisibleBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(currentVisibleBounds));

        int invisibleLeft = currentVisibleBounds.Left - currentRawBounds.Left;
        int invisibleTop = currentVisibleBounds.Top - currentRawBounds.Top;
        int invisibleRight = currentRawBounds.Right - currentVisibleBounds.Right;
        int invisibleBottom = currentRawBounds.Bottom - currentVisibleBounds.Bottom;
        return new SourceWindowBounds(
            checked(targetVisibleBounds.Left - invisibleLeft),
            checked(targetVisibleBounds.Top - invisibleTop),
            checked(targetVisibleBounds.Right + invisibleRight),
            checked(targetVisibleBounds.Bottom + invisibleBottom));
    }
}

public static class Window4KStagingPolicy
{
    public static bool IsProcessEligible(uint processId) => processId != 0;

    public static bool IsSameWindowProcess(uint expectedProcessId, uint actualProcessId) =>
        expectedProcessId != 0 && actualProcessId == expectedProcessId;
}

/// <summary>
/// Temporarily moves one top-level window to the MTT display while preserving its logical
/// size across the source and target monitor DPI densities.
/// WINDOWPLACEMENT and the DWM-visible bounds are captured before mutation and restored by lease.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWindow4KStager : IWindow4KStager
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(180);

    public IWindow4KStagingRequest Prepare(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
            throw new ArgumentException("A target window is required.", nameof(windowHandle));

        nint root = GetAncestor(windowHandle, GetAncestorRoot);
        if (root == nint.Zero)
            root = windowHandle;
        uint processId = EnsureEligible(root);

        var originalPlacement = WindowPlacement.Create();
        if (!GetWindowPlacement(root, ref originalPlacement))
            throw LastWin32("read the target window placement");
        var originalBounds = ReadVisibleBounds(root);
        var sourceDisplayBounds = ReadMonitorBounds(root);
        uint sourceDpi = GetDpiForWindow(root);
        if (sourceDpi == 0)
            throw LastWin32("read the target window DPI");
        return new StagingRequest(
            root,
            processId,
            originalPlacement,
            originalBounds,
            sourceDisplayBounds,
            sourceDpi / 96.0);
    }

    public async Task<IStagedWindowLease> StageAsync(
        IWindow4KStagingRequest request,
        DisplayBounds targetBounds,
        double targetDpiScale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request is not StagingRequest prepared)
            throw new ArgumentException("The staging request belongs to another adapter.", nameof(request));
        if (Interlocked.Exchange(ref prepared.Consumed, 1) != 0)
            throw new InvalidOperationException("The window staging request has already been used.");
        if (targetBounds.Width <= 0 || targetBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBounds));
        if (!double.IsFinite(targetDpiScale) || targetDpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetDpiScale));

        nint root = prepared.WindowHandle;
        EnsureEligible(root, prepared.ProcessId);
        double sourceDpiScale = prepared.SourceDpiScale;
        if (targetDpiScale <= sourceDpiScale + 0.001)
        {
            throw new InvalidOperationException(
                $"The 4K display scale is {targetDpiScale:P0}, but the source window scale is " +
                $"{sourceDpiScale:P0}. Set the virtual display to a higher scale (normally 200%) " +
                "to gain capture density without enlarging the app layout.");
        }
        var stagedBounds = WindowDensityPlacement.CalculateVisibleBounds(
            prepared.OriginalBounds,
            prepared.SourceDisplayBounds,
            sourceDpiScale,
            targetBounds,
            targetDpiScale);
        var lease = new Lease(
            root,
            prepared.ProcessId,
            prepared.OriginalPlacement,
            prepared.OriginalBounds,
            stagedBounds);
        try
        {
            _ = ShowWindowAsync(root, ShowNormal);
            await FitVisibleBoundsAsync(
                root,
                prepared.ProcessId,
                stagedBounds,
                cancellationToken).ConfigureAwait(false);
            return lease;
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static uint EnsureEligible(nint window, uint? expectedProcessId = null)
    {
        if (!IsWindow(window) || !IsWindowVisible(window))
            throw new InvalidOperationException("The target window is no longer visible.");
        _ = GetWindowThreadProcessId(window, out uint processId);
        if (!Window4KStagingPolicy.IsProcessEligible(processId))
            throw new InvalidOperationException("The target window has no owning process.");
        if (expectedProcessId is { } expected
            && !Window4KStagingPolicy.IsSameWindowProcess(expected, processId))
            throw new InvalidOperationException("The selected HWND no longer belongs to the frozen process.");
        return processId;
    }

    private static async Task FitVisibleBoundsAsync(
        nint window,
        uint expectedProcessId,
        SourceWindowBounds target,
        CancellationToken cancellationToken)
    {
        const int requiredStableObservations = 3;
        int stableObservations = 0;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureEligible(window, expectedProcessId);
            var raw = ReadRawBounds(window);
            var visible = ReadVisibleBounds(window);
            if (visible != target)
            {
                stableObservations = 0;
                var desiredRaw = WindowFramePlacement.CalculateRawBounds(target, raw, visible);
                if (!SetWindowPos(
                        window,
                        nint.Zero,
                        desiredRaw.Left,
                        desiredRaw.Top,
                        desiredRaw.Width,
                        desiredRaw.Height,
                        SetWindowPositionFlags))
                {
                    throw LastWin32("move the target window to the 4K display");
                }
            }

            await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
            _ = DwmFlush();
            if (ReadVisibleBounds(window) == target)
                stableObservations++;
            else
                stableObservations = 0;
            if (stableObservations >= requiredStableObservations)
                return;
        }

        var actual = ReadVisibleBounds(window);
        throw new InvalidOperationException(
            $"The target window refused the requested 4K bounds; actual visible size is " +
            $"{actual.Width}×{actual.Height}.");
    }

    private static SourceWindowBounds ReadRawBounds(nint window)
    {
        if (!GetWindowRect(window, out var rect))
            throw LastWin32("read the target window bounds");
        return rect.ToBounds();
    }

    private static SourceWindowBounds ReadVisibleBounds(nint window)
    {
        if (DwmGetWindowAttribute(
                window,
                DwmExtendedFrameBounds,
                out var rect,
                Marshal.SizeOf<NativeRect>()) == 0
            && rect.Right > rect.Left
            && rect.Bottom > rect.Top)
        {
            return rect.ToBounds();
        }
        return ReadRawBounds(window);
    }

    private static DisplayBounds ReadMonitorBounds(nint window)
    {
        nint monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
            throw LastWin32("locate the target window monitor");
        var info = MonitorInfo.Create();
        if (!GetMonitorInfo(monitor, ref info))
            throw LastWin32("read the target window monitor bounds");
        return new DisplayBounds(
            info.Monitor.Left,
            info.Monitor.Top,
            checked(info.Monitor.Right - info.Monitor.Left),
            checked(info.Monitor.Bottom - info.Monitor.Top));
    }

    private static Win32Exception LastWin32(string operation) => new(
        Marshal.GetLastWin32Error(),
        $"Windows could not {operation}.");

    private sealed class Lease : IStagedWindowLease
    {
        private readonly uint _processId;
        private readonly WindowPlacement _originalPlacement;
        private int _disposed;

        public Lease(
            nint windowHandle,
            uint processId,
            WindowPlacement originalPlacement,
            SourceWindowBounds originalBounds,
            SourceWindowBounds stagedBounds)
        {
            WindowHandle = windowHandle;
            _processId = processId;
            _originalPlacement = originalPlacement;
            OriginalBounds = originalBounds;
            StagedBounds = stagedBounds;
        }

        public nint WindowHandle { get; }
        public SourceWindowBounds OriginalBounds { get; }
        public SourceWindowBounds StagedBounds { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || !IsWindow(WindowHandle))
                return;
            _ = GetWindowThreadProcessId(WindowHandle, out uint currentProcessId);
            if (!Window4KStagingPolicy.IsSameWindowProcess(_processId, currentProcessId))
                return;

            var placement = _originalPlacement;
            if (!SetWindowPlacement(WindowHandle, ref placement))
                throw LastWin32("restore the target window placement");
            _ = ShowWindowAsync(WindowHandle, checked((int)placement.ShowCommand));
            await Task.Delay(SettleDelay).ConfigureAwait(false);
            _ = DwmFlush();
            // Per-monitor-aware UI frameworks can apply WM_DPICHANGED after the placement API
            // has already returned. Hold the original visible bounds until that asynchronous
            // DPI response has settled, just as staging does on the 4K display.
            await FitVisibleBoundsAsync(
                WindowHandle,
                _processId,
                OriginalBounds,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class StagingRequest : IWindow4KStagingRequest
    {
        public StagingRequest(
            nint windowHandle,
            uint processId,
            WindowPlacement originalPlacement,
            SourceWindowBounds originalBounds,
            DisplayBounds sourceDisplayBounds,
            double sourceDpiScale)
        {
            WindowHandle = windowHandle;
            ProcessId = processId;
            OriginalPlacement = originalPlacement;
            OriginalBounds = originalBounds;
            SourceDisplayBounds = sourceDisplayBounds;
            SourceDpiScale = sourceDpiScale;
        }

        public nint WindowHandle { get; }
        public uint ProcessId { get; }
        public WindowPlacement OriginalPlacement { get; }
        public SourceWindowBounds OriginalBounds { get; }
        public DisplayBounds SourceDisplayBounds { get; }
        public double SourceDpiScale { get; }
        public int Consumed;
    }

    private const uint GetAncestorRoot = 2;
    private const uint MonitorDefaultToNearest = 2;
    private const int ShowNormal = 1;
    private const uint DwmExtendedFrameBounds = 9;
    private const uint SetWindowPositionFlags =
        0x0004 // SWP_NOZORDER
        | 0x0010 // SWP_NOACTIVATE
        | 0x0020 // SWP_FRAMECHANGED
        | 0x0040 // SWP_SHOWWINDOW
        | 0x4000; // SWP_ASYNCWINDOWPOS

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly SourceWindowBounds ToBounds() =>
            new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public NativePoint MinimumPosition;
        public NativePoint MaximumPosition;
        public NativeRect NormalPosition;
        public NativeRect Device;

        public static WindowPlacement Create() => new()
        {
            Length = (uint)Marshal.SizeOf<WindowPlacement>()
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;

        public static MonitorInfo Create() => new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
