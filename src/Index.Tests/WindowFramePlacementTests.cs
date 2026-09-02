using Index.Platform;

namespace Index.Tests;

public sealed class WindowFramePlacementTests
{
    [Fact]
    public void MapsWindowToHigherDensityDisplayWithoutChangingLogicalSize()
    {
        var staged = WindowDensityPlacement.CalculateVisibleBounds(
            new SourceWindowBounds(100, 100, 1300, 900),
            new DisplayBounds(0, 0, 1920, 1080),
            1,
            new DisplayBounds(1920, 0, 3840, 2160),
            2);

        Assert.Equal(new SourceWindowBounds(2120, 200, 4520, 1800), staged);
    }

    [Fact]
    public void ClampsDensityMappedWindowInsideTargetDisplay()
    {
        var staged = WindowDensityPlacement.CalculateVisibleBounds(
            new SourceWindowBounds(1700, 700, 2100, 1000),
            new DisplayBounds(0, 0, 1920, 1080),
            1,
            new DisplayBounds(1920, 0, 3840, 2160),
            2);

        Assert.Equal(new SourceWindowBounds(4960, 1400, 5760, 2000), staged);
    }

    [Fact]
    public void PreservesDensityWhenWindowExtendsPastTargetDisplay()
    {
        var staged = WindowDensityPlacement.CalculateVisibleBounds(
            new SourceWindowBounds(0, 0, 1920, 1152),
            new DisplayBounds(0, 0, 1920, 1200),
            1,
            new DisplayBounds(1920, 0, 3840, 2160),
            2);

        Assert.Equal(new SourceWindowBounds(1920, 0, 5760, 2304), staged);
    }

    [Fact]
    public void CompensatesForInvisibleResizeBorders()
    {
        var targetVisible = new SourceWindowBounds(1920, 0, 5760, 2160);
        var currentRaw = new SourceWindowBounds(90, 90, 1110, 810);
        var currentVisible = new SourceWindowBounds(100, 100, 1100, 800);

        var raw = WindowFramePlacement.CalculateRawBounds(
            targetVisible,
            currentRaw,
            currentVisible);

        Assert.Equal(new SourceWindowBounds(1910, -10, 5770, 2170), raw);
    }

    [Fact]
    public void RejectsEmptySourceBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowFramePlacement.CalculateRawBounds(
                new SourceWindowBounds(0, 0, 3840, 2160),
                new SourceWindowBounds(0, 0, 0, 0),
                new SourceWindowBounds(0, 0, 100, 100)));
    }

    [Fact]
    public void AllowsWindowsOwnedByTheCurrentProcess()
    {
        uint currentProcessId = checked((uint)Environment.ProcessId);

        Assert.True(Window4KStagingPolicy.IsProcessEligible(currentProcessId));
        Assert.True(Window4KStagingPolicy.IsSameWindowProcess(
            currentProcessId,
            currentProcessId));
    }

    [Fact]
    public void RejectsMissingOrChangedWindowProcessIdentity()
    {
        Assert.False(Window4KStagingPolicy.IsProcessEligible(0));
        Assert.False(Window4KStagingPolicy.IsSameWindowProcess(10, 20));
        Assert.False(Window4KStagingPolicy.IsSameWindowProcess(0, 0));
    }
}
