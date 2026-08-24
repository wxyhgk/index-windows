namespace Index.Platform;

public sealed record BrowserWindowCaptureTarget(nint Handle, SourceWindowBounds Bounds);

/// <summary>Pure routing policy for compositor-direct browser window capture.</summary>
public static class BrowserWindowCapturePolicy
{
    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "vivaldi", "opera", "opera_gx", "firefox"
    };

    public static BrowserWindowCaptureTarget? FindTarget(
        SourceApplicationSnapshot snapshot,
        SourceWindowBounds selectedRegion,
        SourceApplicationInfo? sourceApplication,
        int edgeTolerance = 3)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsSupportedBrowser(sourceApplication) || edgeTolerance < 0)
            return null;

        var hit = SourceWindowMatcher.FindBestMatch(snapshot.Windows, selectedRegion);
        if (hit is null) return null;

        nint rootHandle = hit.RootHandle != 0 ? hit.RootHandle : hit.Handle;
        var root = snapshot.Windows.FirstOrDefault(candidate =>
            candidate.IsTopLevel && candidate.Handle == rootHandle);
        if (root is null || !SameBounds(root.Bounds, selectedRegion, edgeTolerance))
            return null;

        return new BrowserWindowCaptureTarget(root.Handle, root.Bounds);
    }

    public static bool IsSupportedBrowser(SourceApplicationInfo? application)
    {
        if (application is null) return false;
        string identifier = application.AppIdentifier ?? string.Empty;
        string processName = Path.GetFileNameWithoutExtension(identifier);
        if (BrowserProcessNames.Contains(processName)) return true;

        string appName = application.AppName ?? string.Empty;
        return appName.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            || appName.Contains("Edge", StringComparison.OrdinalIgnoreCase)
            || appName.Contains("Brave", StringComparison.OrdinalIgnoreCase)
            || appName.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)
            || appName.Contains("Opera", StringComparison.OrdinalIgnoreCase)
            || appName.Contains("Firefox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameBounds(
        SourceWindowBounds first,
        SourceWindowBounds second,
        int tolerance) =>
        Math.Abs(first.Left - second.Left) <= tolerance
        && Math.Abs(first.Top - second.Top) <= tolerance
        && Math.Abs(first.Right - second.Right) <= tolerance
        && Math.Abs(first.Bottom - second.Bottom) <= tolerance;
}
