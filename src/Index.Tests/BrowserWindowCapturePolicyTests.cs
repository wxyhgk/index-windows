using Index.Platform;

namespace Index.Tests;

public sealed class BrowserWindowCapturePolicyTests
{
    [Fact]
    public void SelectsBrowserRootForExactWholeWindowSelection()
    {
        var bounds = new SourceWindowBounds(100, 80, 1100, 780);
        var snapshot = Snapshot(
            new SourceWindowInfo(2, 10, "content", new(120, 140, 1080, 760), 1, 1),
            new SourceWindowInfo(1, 10, "page", bounds, 1, 0));
        var application = new SourceApplicationInfo(10, "Microsoft Edge", @"C:\Edge\msedge.exe", "page");

        var target = BrowserWindowCapturePolicy.FindTarget(snapshot, bounds, application);

        Assert.NotNull(target);
        Assert.Equal((nint)1, target.Handle);
        Assert.Equal(bounds, target.Bounds);
    }

    [Fact]
    public void AllowsSmallCoordinateRoundingDifference()
    {
        var bounds = new SourceWindowBounds(100, 80, 1100, 780);
        var target = BrowserWindowCapturePolicy.FindTarget(
            Snapshot(new SourceWindowInfo(1, 10, "page", bounds, 1, 0)),
            new SourceWindowBounds(102, 79, 1098, 782),
            new SourceApplicationInfo(10, "Chrome", "chrome", "page"));

        Assert.NotNull(target);
    }

    [Fact]
    public void RejectsPartialBrowserSelectionAndOrdinaryApplication()
    {
        var bounds = new SourceWindowBounds(100, 80, 1100, 780);
        var snapshot = Snapshot(new SourceWindowInfo(1, 10, "page", bounds, 1, 0));

        Assert.Null(BrowserWindowCapturePolicy.FindTarget(
            snapshot,
            new SourceWindowBounds(200, 180, 800, 600),
            new SourceApplicationInfo(10, "Microsoft Edge", "msedge", "page")));
        Assert.Null(BrowserWindowCapturePolicy.FindTarget(
            snapshot,
            bounds,
            new SourceApplicationInfo(10, "Notepad", "notepad.exe", "text")));
    }

    private static SourceApplicationSnapshot Snapshot(params SourceWindowInfo[] windows) =>
        new(null, windows);
}
