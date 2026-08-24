using Index.Capture;

namespace Index.Platform;

/// <summary>Window-source context frozen when capture starts.</summary>
public sealed record SourceApplicationSnapshot(
    SourceApplicationInfo? ForegroundApplication,
    IReadOnlyList<SourceWindowInfo> Windows);

/// <summary>Windows application source metadata that can be persisted with a capture.</summary>
public sealed record SourceApplicationInfo(
    uint ProcessId,
    string? AppName,
    string? AppIdentifier,
    string? WindowTitle);

/// <summary>
/// A hittable window in priority order. Child HWNDs precede their owning top-level HWND while
/// separate top-level windows retain EnumWindows Z order. Coordinates use virtual-desktop pixels.
/// </summary>
public sealed record SourceWindowInfo(
    nint Handle,
    uint ProcessId,
    string? WindowTitle,
    SourceWindowBounds Bounds,
    nint RootHandle = default,
    int HierarchyDepth = 0)
{
    public bool IsTopLevel => HierarchyDepth == 0;
}

public readonly record struct SourceWindowBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(int x, int y) =>
        x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Intersects(SourceWindowBounds other) =>
        Left < other.Right && Right > other.Left &&
        Top < other.Bottom && Bottom > other.Top;
}

/// <summary>
/// Pure filtering policy for native screenshot targets. The Win32 adapter supplies the live
/// window state while tests can exercise the rejection rules without creating desktop windows.
/// </summary>
public static class WindowsWindowCandidatePolicy
{
    public const uint VisibleStyle = 0x10000000;
    public const uint TransparentExtendedStyle = 0x00000020;
    public const uint NoRedirectionBitmapExtendedStyle = 0x00200000;

    public static bool ShouldInclude(
        bool isShellWindow,
        bool isVisible,
        bool isCloaked,
        bool isMinimized,
        uint style,
        uint extendedStyle,
        SourceWindowBounds bounds)
    {
        if (!IsEligibleSurface(
                isShellWindow,
                isVisible,
                isCloaked,
                isMinimized,
                style,
                extendedStyle))
            return false;

        // Minimized windows commonly report this legacy sentinel even when their other state is
        // racing the enumeration callback. Reject it independently of IsIconic.
        if (bounds.Left == -32000 && bounds.Top == -32000)
            return false;

        return bounds.Width > 20 && bounds.Height > 20;
    }

    public static bool IsEligibleSurface(
        bool isShellWindow,
        bool isVisible,
        bool isCloaked,
        bool isMinimized,
        uint style,
        uint extendedStyle) =>
        !isShellWindow
        && isVisible
        && !isCloaked
        && !isMinimized
        && (style & VisibleStyle) != 0
        && (extendedStyle & (TransparentExtendedStyle | NoRedirectionBitmapExtendedStyle)) == 0;
}

/// <summary>A top-level window boundary converted to one overlay's logical coordinate space.</summary>
public sealed record WindowSelectionTarget(
    nint Handle,
    SourceWindowBounds PhysicalBounds,
    SelectionRect Bounds,
    int HierarchyDepth = 0)
{
    public bool IsTopLevel => HierarchyDepth == 0;
    public bool IsPixelRegion => Handle == 0;
}

/// <summary>
/// Converts frozen window topology into display-local targets. The mapper remains outside the
/// overlay so UI code only handles pointer state and never queries live windows.
/// </summary>
public static class WindowSelectionTargetMapper
{
    public static IReadOnlyList<WindowSelectionTarget> Create(
        DisplaySnapshot display,
        IReadOnlyList<SourceWindowInfo> windows,
        uint excludedProcessId)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(windows);

        var displayBounds = new SourceWindowBounds(
            display.Left,
            display.Top,
            display.Left + display.Width,
            display.Top + display.Height);
        var scale = Math.Max(display.DpiScale, 0.01);
        var result = new List<WindowSelectionTarget>(windows.Count);

        foreach (var window in windows)
        {
            if (window.ProcessId == excludedProcessId) continue;

            var clipped = Intersect(window.Bounds, displayBounds);
            if (clipped.Width <= 20 || clipped.Height <= 20) continue;

            result.Add(new WindowSelectionTarget(
                window.Handle,
                window.Bounds,
                new SelectionRect(
                    (clipped.Left - display.Left) / scale,
                    (clipped.Top - display.Top) / scale,
                    clipped.Width / scale,
                    clipped.Height / scale),
                window.HierarchyDepth));
        }

        return result;
    }

    public static WindowSelectionTarget? HitTest(
        IReadOnlyList<WindowSelectionTarget> targets,
        SelectionPoint point)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return targets.FirstOrDefault(target =>
            point.X >= target.Bounds.Left && point.X < target.Bounds.Right
            && point.Y >= target.Bounds.Top && point.Y < target.Bounds.Bottom);
    }

    private static SourceWindowBounds Intersect(SourceWindowBounds first, SourceWindowBounds second) => new(
        Math.Max(first.Left, second.Left),
        Math.Max(first.Top, second.Top),
        Math.Min(first.Right, second.Right),
        Math.Min(first.Bottom, second.Bottom));
}

public interface ISourceApplicationResolver
{
    SourceApplicationSnapshot CaptureSnapshot();

    SourceApplicationInfo? Resolve(
        SourceApplicationSnapshot snapshot,
        SourceWindowBounds selectedRegion);
}

/// <summary>Platform-independent center-first window matching policy.</summary>
public static class SourceWindowMatcher
{
    public static SourceWindowInfo? FindBestMatch(
        IReadOnlyList<SourceWindowInfo> windows,
        SourceWindowBounds selectedRegion)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var centerX = selectedRegion.Left + selectedRegion.Width / 2;
        var centerY = selectedRegion.Top + selectedRegion.Height / 2;
        return windows.FirstOrDefault(candidate =>
                candidate.Bounds.Contains(centerX, centerY))
            ?? windows.FirstOrDefault(candidate =>
                candidate.Bounds.Intersects(selectedRegion));
    }
}
