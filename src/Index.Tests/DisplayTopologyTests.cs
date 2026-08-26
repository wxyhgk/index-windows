using Index.Platform;

namespace Index.Tests;

public sealed class DisplayTopologyTests
{
    [Fact]
    public void MatchesNegativeCoordinatesAndIgnoresEnumerationOrder()
    {
        var expected = Snapshot(
            Display("primary", "Internal", 0, 0, 1920, 1080, 1.25, true),
            Display("left", "External", -2560, -240, 2560, 1440, 1.0));
        var reordered = Snapshot(
            Display("LEFT", "External", -2560, -240, 2560, 1440, 1.0),
            Display("PRIMARY", "Internal", 0, 0, 1920, 1080, 1.25, true));

        Assert.True(DisplayTopology.Matches(expected, reordered));
    }

    [Fact]
    public void RejectsDpiChange()
    {
        var expected = Snapshot(Display("one", "Display", 0, 0, 1920, 1080, 1.0, true));
        var changed = Snapshot(Display("one", "Display", 0, 0, 1920, 1080, 1.25, true));

        Assert.False(DisplayTopology.Matches(expected, changed));
    }

    [Fact]
    public void RejectsBoundsChange()
    {
        var expected = Snapshot(Display("one", "Display", 0, 0, 1920, 1080, 1.0, true));
        var moved = Snapshot(Display("one", "Display", -1920, 0, 1920, 1080, 1.0, true));
        var rotated = Snapshot(Display("one", "Display", 0, 0, 1080, 1920, 1.0, true));

        Assert.False(DisplayTopology.Matches(expected, moved));
        Assert.False(DisplayTopology.Matches(expected, rotated));
    }

    [Fact]
    public void RejectsDisplayCountChange()
    {
        var expected = Snapshot(
            Display("one", "Internal", 0, 0, 1920, 1080, 1.0, true),
            Display("two", "External", 1920, 0, 1920, 1080, 1.0));
        var disconnected = Snapshot(Display("one", "Internal", 0, 0, 1920, 1080, 1.0, true));

        Assert.False(DisplayTopology.Matches(expected, disconnected));
    }

    [Fact]
    public void RejectsDuplicateStableIdentity()
    {
        var invalid = Snapshot(
            Display("same", "A", 0, 0, 100, 100, 1.0, true),
            Display("same", "B", 100, 0, 100, 100, 1.0));

        Assert.False(DisplayTopology.Matches(invalid, invalid));
    }

    [Fact]
    public void CapturedDisplayCanMatchWithinALargerCurrentTopology()
    {
        var captured = Snapshot(
            Display("secondary", "External", 0, 1080, 1920, 1200, 1.0));
        var current = Snapshot(
            Display("primary", "Internal", 0, 0, 1920, 1080, 1.0, true),
            Display("SECONDARY", "External", 0, 1080, 1920, 1200, 1.0));

        Assert.True(DisplayTopology.ContainsMatchingDisplays(captured, current));
    }

    [Fact]
    public void CapturedDisplayRejectsCoordinateChangesWithinLargerTopology()
    {
        var captured = Snapshot(
            Display("secondary", "External", 0, 1080, 1920, 1200, 1.0));
        var current = Snapshot(
            Display("primary", "Internal", 0, 0, 1920, 1080, 1.0, true),
            Display("secondary", "External", 1920, 0, 1920, 1200, 1.0));

        Assert.False(DisplayTopology.ContainsMatchingDisplays(captured, current));
    }

    private static DisplayTopologySnapshot Snapshot(params DisplayTopologyEntry[] displays)
        => new(displays);

    private static DisplayTopologyEntry Display(
        string id,
        string name,
        int left,
        int top,
        int width,
        int height,
        double scale,
        bool primary = false)
        => new(id, name, new DisplayBounds(left, top, width, height), scale, primary);
}
