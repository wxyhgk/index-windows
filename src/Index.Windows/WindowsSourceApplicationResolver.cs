using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Index.Platform;

/// <summary>
/// Win32 来源应用适配器。所有窗口枚举、进程信息与标题读取都收口在平台层，
/// Capture 协调器只消费可移植的来源值。
/// </summary>
public sealed class WindowsSourceApplicationResolver : ISourceApplicationResolver
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwChild = 5;
    private const uint GwHwndNext = 2;
    private const int MaximumChildDepth = 32;
    private const int MaximumChildCandidatesPerRoot = 512;
    private const int MaximumVisitedChildWindowsPerRoot = 2048;
    private const uint DwmwaExtendedFrameBounds = 9;
    private const uint DwmwaCloaked = 14;
    private readonly nint _shellWindow = GetShellWindow();

    public SourceApplicationSnapshot CaptureSnapshot()
    {
        var foregroundWindow = GetForegroundWindow();
        var windows = EnumerateWindows();
        SourceApplicationInfo? foreground = null;

        if (foregroundWindow != 0)
        {
            GetWindowThreadProcessId(foregroundWindow, out var processId);
            foreground = ResolveProcess(
                processId,
                Normalize(GetWindowTitle(foregroundWindow)));
        }

        return new SourceApplicationSnapshot(foreground, windows, foregroundWindow);
    }

    public SourceApplicationInfo? Resolve(
        SourceApplicationSnapshot snapshot,
        SourceWindowBounds selectedRegion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var window = SourceWindowMatcher.FindBestMatch(
            snapshot.Windows,
            selectedRegion);

        if (window is null)
            return snapshot.ForegroundApplication;

        return ResolveProcess(window.ProcessId, window.WindowTitle)
            ?? snapshot.ForegroundApplication;
    }

    private IReadOnlyList<SourceWindowInfo> EnumerateWindows()
    {
        var result = new List<SourceWindowInfo>();
        EnumWindows((window, _) =>
        {
            if (!TryGetVisualWindowBounds(window, out var bounds))
                return true;

            var normalizedBounds = new SourceWindowBounds(
                bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            if (!WindowsWindowCandidatePolicy.ShouldInclude(
                    window == _shellWindow,
                    IsWindowVisible(window),
                    IsWindowCloaked(window),
                    IsIconic(window),
                    GetWindowStyle(window, GwlStyle),
                    GetWindowStyle(window, GwlExStyle),
                    normalizedBounds))
                return true;

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
                return true;

            var windowTitle = Normalize(GetWindowTitle(window));
            AppendChildWindows(
                result,
                window,
                normalizedBounds,
                processId,
                windowTitle);
            result.Add(new SourceWindowInfo(
                window,
                processId,
                windowTitle,
                normalizedBounds,
                window,
                HierarchyDepth: 0));
            return true;
        }, 0);
        return result;
    }

    private static void AppendChildWindows(
        List<SourceWindowInfo> result,
        nint rootWindow,
        SourceWindowBounds rootBounds,
        uint rootProcessId,
        string? rootWindowTitle)
    {
        // GetWindow(GW_CHILD/GW_HWNDNEXT) lets us walk direct siblings in their native Z order.
        // Recursing before adding the parent makes the smallest native control win hit testing,
        // without allowing a child of a background top-level window to jump ahead of a foreground
        // top-level window.
        var visited = new HashSet<nint> { rootWindow };
        var seenBounds = new HashSet<WindowCandidateIdentity>();
        var remaining = MaximumChildCandidatesPerRoot;

        AppendChildren(rootWindow, depth: 1);

        void AppendChildren(nint parent, int depth)
        {
            if (depth > MaximumChildDepth || remaining <= 0)
                return;

            var child = GetWindow(parent, GwChild);
            while (child != 0
                   && remaining > 0
                   && visited.Count <= MaximumVisitedChildWindowsPerRoot)
            {
                // Read the sibling before recursing: malformed or rapidly changing foreign HWND
                // trees must not trap capture startup in a cycle.
                var next = GetWindow(child, GwHwndNext);
                if (visited.Add(child) && IsEligibleWindowSurface(child))
                {
                    AppendChildren(child, depth + 1);

                    if (TryGetClippedChildBounds(child, rootBounds, out var childBounds))
                    {
                        // Attribute hosted/cross-process child HWNDs to their owning top-level app.
                        // Otherwise WebView/render helpers could be persisted as the screenshot
                        // source instead of the visible application that owns the frame.
                        var identity = new WindowCandidateIdentity(rootProcessId, childBounds);
                        if (seenBounds.Add(identity))
                        {
                            result.Add(new SourceWindowInfo(
                                child,
                                rootProcessId,
                                Normalize(GetWindowTitle(child)) ?? rootWindowTitle,
                                childBounds,
                                rootWindow,
                                depth));
                            remaining--;
                        }
                    }
                }

                child = next;
            }
        }
    }

    private static bool TryGetClippedChildBounds(
        nint child,
        SourceWindowBounds rootBounds,
        out SourceWindowBounds bounds)
    {
        bounds = default;
        if (!TryGetVisualWindowBounds(child, out var rawBounds))
            return false;

        var visualBounds = new SourceWindowBounds(
            Math.Max(rawBounds.Left, rootBounds.Left),
            Math.Max(rawBounds.Top, rootBounds.Top),
            Math.Min(rawBounds.Right, rootBounds.Right),
            Math.Min(rawBounds.Bottom, rootBounds.Bottom));
        if (!WindowsWindowCandidatePolicy.ShouldInclude(
                isShellWindow: false,
                IsWindowVisible(child),
                IsWindowCloaked(child),
                IsIconic(child),
                GetWindowStyle(child, GwlStyle),
                GetWindowStyle(child, GwlExStyle),
                visualBounds))
            return false;

        // A child that is effectively the entire top-level frame does not provide a useful extra
        // target. This also removes common framework host HWNDs stacked at identical coordinates.
        if (visualBounds == rootBounds)
            return false;

        bounds = visualBounds;
        return true;
    }

    private readonly record struct WindowCandidateIdentity(
        uint ProcessId,
        SourceWindowBounds Bounds);

    private static SourceApplicationInfo? ResolveProcess(uint processId, string? windowTitle)
    {
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var processName = Normalize(process.ProcessName);
            string? executablePath = null;
            try { executablePath = Normalize(process.MainModule?.FileName); }
            catch { /* 受保护或不同位数的进程可能拒绝模块查询。 */ }

            string? displayName = null;
            if (executablePath is not null)
            {
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(executablePath);
                    displayName = Normalize(version.FileDescription)
                        ?? Normalize(version.ProductName);
                }
                catch { /* 版本资源不是来源识别的硬依赖。 */ }
            }

            return new SourceApplicationInfo(
                processId,
                displayName ?? processName,
                executablePath ?? processName,
                windowTitle);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWindowTitle(nint window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0) return null;
        var buffer = new StringBuilder(length + 1);
        return GetWindowText(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : null;
    }

    private static bool IsWindowCloaked(nint window)
    {
        var cloaked = 0;
        return DwmGetWindowAttribute(
                window,
                DwmwaCloaked,
                out cloaked,
                Marshal.SizeOf<int>()) == 0
            && cloaked != 0;
    }

    private static bool TryGetVisualWindowBounds(nint window, out Rect bounds)
    {
        // DWM's extended frame is what the user can actually see. GetWindowRect can include
        // invisible resize margins, which makes the automatic border appear several pixels off.
        if (DwmGetWindowAttribute(
                window,
                DwmwaExtendedFrameBounds,
                out bounds,
                Marshal.SizeOf<Rect>()) == 0
            && bounds.Right > bounds.Left
            && bounds.Bottom > bounds.Top)
            return true;

        return GetWindowRect(window, out bounds);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static uint GetWindowStyle(nint window, int index) =>
        unchecked((uint)GetWindowLongPtr(window, index).ToInt64());

    private static bool IsEligibleWindowSurface(nint window) =>
        WindowsWindowCandidatePolicy.IsEligibleSurface(
            isShellWindow: false,
            IsWindowVisible(window),
            IsWindowCloaked(window),
            IsIconic(window),
            GetWindowStyle(window, GwlStyle),
            GetWindowStyle(window, GwlExStyle));

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint window,
        uint attribute,
        out Rect value,
        int valueSize);
}
