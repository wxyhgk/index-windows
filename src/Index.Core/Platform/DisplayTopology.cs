namespace Index.Platform;

/// <summary>Physical-pixel bounds in the Windows virtual desktop coordinate space.</summary>
public readonly record struct DisplayBounds(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
}

/// <summary>Immutable monitor description used for topology validation.</summary>
public sealed record DisplayTopologyEntry(
    string DisplayId,
    string DeviceName,
    DisplayBounds Bounds,
    double DpiScale,
    bool IsPrimary);

/// <summary>A point-in-time set of active displays. Input order is deliberately not meaningful.</summary>
public sealed class DisplayTopologySnapshot
{
    public DisplayTopologySnapshot(IEnumerable<DisplayTopologyEntry> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        Displays = displays.ToArray();
    }

    public IReadOnlyList<DisplayTopologyEntry> Displays { get; }

    public static DisplayTopologySnapshot FromSnapshots(IEnumerable<DisplaySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        return new DisplayTopologySnapshot(snapshots.Select((snapshot, index) =>
            new DisplayTopologyEntry(
                snapshot.DisplayId,
                snapshot.DeviceName,
                new DisplayBounds(snapshot.Left, snapshot.Top, snapshot.Width, snapshot.Height),
                snapshot.DpiScale,
                index == 0)));
    }
}

/// <summary>
/// Compares all topology-defining properties. A stable ID alone is insufficient because moving,
/// rotating or rescaling a display invalidates the frozen bitmap's coordinate mapping.
/// </summary>
public static class DisplayTopology
{
    private const double ScaleTolerance = 0.000001;

    public static bool Matches(DisplayTopologySnapshot expected, DisplayTopologySnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return Matches(expected.Displays, actual.Displays);
    }

    public static bool Matches(
        IReadOnlyCollection<DisplayTopologyEntry> expected,
        IReadOnlyCollection<DisplayTopologyEntry> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Count != actual.Count) return false;

        var expectedById = UniqueById(expected);
        var actualById = UniqueById(actual);
        if (expectedById is null || actualById is null || expectedById.Count != actualById.Count)
            return false;

        foreach (var (id, expectedDisplay) in expectedById)
        {
            if (!actualById.TryGetValue(id, out var actualDisplay)) return false;
            if (!string.Equals(expectedDisplay.DeviceName, actualDisplay.DeviceName, StringComparison.Ordinal)) return false;
            if (expectedDisplay.Bounds != actualDisplay.Bounds) return false;
            if (Math.Abs(expectedDisplay.DpiScale - actualDisplay.DpiScale) > ScaleTolerance) return false;
            if (expectedDisplay.IsPrimary != actualDisplay.IsPrimary) return false;
        }
        return true;
    }

    private static Dictionary<string, DisplayTopologyEntry>? UniqueById(
        IEnumerable<DisplayTopologyEntry> displays)
    {
        var result = new Dictionary<string, DisplayTopologyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in displays)
        {
            if (string.IsNullOrWhiteSpace(display.DisplayId)
                || !result.TryAdd(display.DisplayId, display))
                return null;
        }
        return result;
    }
}
