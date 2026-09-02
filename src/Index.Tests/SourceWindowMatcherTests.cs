using Index.Capture;
using Index.Platform;

namespace Index.Tests;

public sealed class SourceWindowMatcherTests
{
    [Fact]
    public void WindowCandidatePolicyAcceptsOrdinaryVisibleWindow()
    {
        Assert.True(WindowsWindowCandidatePolicy.ShouldInclude(
            isShellWindow: false,
            isVisible: true,
            isCloaked: false,
            isMinimized: false,
            style: WindowsWindowCandidatePolicy.VisibleStyle,
            extendedStyle: 0,
            new SourceWindowBounds(100, 80, 900, 700)));
    }

    [Fact]
    public void WindowCandidatePolicyRejectsTransparentSurface()
    {
        Assert.False(WindowsWindowCandidatePolicy.ShouldInclude(
            isShellWindow: false,
            isVisible: true,
            isCloaked: false,
            isMinimized: false,
            style: WindowsWindowCandidatePolicy.VisibleStyle,
            extendedStyle: WindowsWindowCandidatePolicy.TransparentExtendedStyle,
            new SourceWindowBounds(100, 80, 900, 700)));
    }

    [Fact]
    public void WindowCandidatePolicyAcceptsNoRedirectionBitmapApplicationWindow()
    {
        Assert.True(WindowsWindowCandidatePolicy.ShouldInclude(
            isShellWindow: false,
            isVisible: true,
            isCloaked: false,
            isMinimized: false,
            style: WindowsWindowCandidatePolicy.VisibleStyle,
            extendedStyle: WindowsWindowCandidatePolicy.NoRedirectionBitmapExtendedStyle,
            new SourceWindowBounds(100, 80, 900, 700)));
    }

    [Fact]
    public void WindowCandidatePolicyRejectsMinimizedSentinelDuringStateRace()
    {
        Assert.False(WindowsWindowCandidatePolicy.ShouldInclude(
            isShellWindow: false,
            isVisible: true,
            isCloaked: false,
            isMinimized: false,
            style: WindowsWindowCandidatePolicy.VisibleStyle,
            extendedStyle: 0,
            new SourceWindowBounds(-32000, -32000, -31000, -31000)));
    }

    [Fact]
    public void PrefersTopmostWindowContainingSelectionCenter()
    {
        var top = Window(1, new SourceWindowBounds(50, 50, 150, 150));
        var back = Window(2, new SourceWindowBounds(0, 0, 300, 300));

        var match = SourceWindowMatcher.FindBestMatch(
            [top, back],
            new SourceWindowBounds(80, 80, 160, 160));

        Assert.Same(top, match);
    }

    [Fact]
    public void FallsBackToTopmostIntersectingWindow()
    {
        var intersecting = Window(1, new SourceWindowBounds(0, 0, 100, 100));
        var containingCenter = Window(2, new SourceWindowBounds(200, 200, 400, 400));

        var match = SourceWindowMatcher.FindBestMatch(
            [intersecting, containingCenter],
            new SourceWindowBounds(90, 90, 190, 190));

        Assert.Same(intersecting, match);
    }

    [Fact]
    public void ReturnsNullWhenNoWindowTouchesSelection()
    {
        var match = SourceWindowMatcher.FindBestMatch(
            [Window(1, new SourceWindowBounds(0, 0, 100, 100))],
            new SourceWindowBounds(200, 200, 300, 300));

        Assert.Null(match);
    }

    [Fact]
    public void TargetMapperConvertsPhysicalPixelsAndClipsToDisplay()
    {
        var display = Display(left: 1920, top: -200, width: 1600, height: 1200, scale: 1.25);
        var windows = new[]
        {
            new SourceWindowInfo(1, 10, "cross-display", new SourceWindowBounds(1800, -100, 2520, 700)),
            new SourceWindowInfo(2, 99, "Index", new SourceWindowBounds(2100, 0, 2300, 200))
        };

        var targets = WindowSelectionTargetMapper.Create(display, windows);

        Assert.Equal(2, targets.Count);
        var target = targets[0];
        Assert.Equal(new SelectionRect(0, 80, 480, 640), target.Bounds);
    }

    [Fact]
    public void TargetHitTestKeepsFrozenZOrder()
    {
        var display = Display(left: 0, top: 0, width: 1000, height: 800, scale: 1);
        var windows = new[]
        {
            new SourceWindowInfo(1, 10, "front", new SourceWindowBounds(100, 100, 600, 600)),
            new SourceWindowInfo(2, 11, "back", new SourceWindowBounds(0, 0, 1000, 800))
        };
        var targets = WindowSelectionTargetMapper.Create(display, windows);

        Assert.Equal((nint)1, WindowSelectionTargetMapper.HitTest(targets, new SelectionPoint(200, 200))?.Handle);
        Assert.Equal((nint)2, WindowSelectionTargetMapper.HitTest(targets, new SelectionPoint(800, 700))?.Handle);
    }

    [Fact]
    public void TargetHitTestPrefersNestedControlBeforeItsTopLevelWindow()
    {
        var display = Display(left: 0, top: 0, width: 1200, height: 900, scale: 1);
        var windows = new[]
        {
            new SourceWindowInfo(
                3,
                10,
                "编辑区域",
                new SourceWindowBounds(180, 160, 700, 600),
                RootHandle: 1,
                HierarchyDepth: 2),
            new SourceWindowInfo(
                2,
                10,
                "内容区域",
                new SourceWindowBounds(120, 100, 760, 650),
                RootHandle: 1,
                HierarchyDepth: 1),
            new SourceWindowInfo(
                1,
                10,
                "应用窗口",
                new SourceWindowBounds(100, 80, 800, 700),
                RootHandle: 1,
                HierarchyDepth: 0)
        };

        var targets = WindowSelectionTargetMapper.Create(display, windows);

        Assert.Equal((nint)3, WindowSelectionTargetMapper.HitTest(targets, new SelectionPoint(300, 300))?.Handle);
        Assert.Equal((nint)2, WindowSelectionTargetMapper.HitTest(targets, new SelectionPoint(150, 140))?.Handle);
        Assert.Equal((nint)1, WindowSelectionTargetMapper.HitTest(targets, new SelectionPoint(110, 90))?.Handle);
    }

    [Fact]
    public void TargetMapperKeepsApplicationWindowsIncludingNestedControls()
    {
        var display = Display(left: 0, top: 0, width: 1000, height: 800, scale: 1);
        var windows = new[]
        {
            new SourceWindowInfo(
                2,
                99,
                "Index 控件",
                new SourceWindowBounds(120, 120, 500, 400),
                RootHandle: 1,
                HierarchyDepth: 1),
            new SourceWindowInfo(
                1,
                99,
                "Index",
                new SourceWindowBounds(100, 100, 600, 500),
                RootHandle: 1,
                HierarchyDepth: 0),
            new SourceWindowInfo(
                3,
                10,
                "浏览器",
                new SourceWindowBounds(0, 0, 900, 700),
                RootHandle: 3,
                HierarchyDepth: 0)
        };

        var targets = WindowSelectionTargetMapper.Create(display, windows);

        Assert.Equal(new nint[] { 2, 1, 3 }, targets.Select(target => target.Handle));
    }

    private static DisplaySnapshot Display(int left, int top, int width, int height, double scale) => new()
    {
        DisplayId = "display",
        DeviceName = "test",
        DisplayIndex = 0,
        Left = left,
        Top = top,
        Width = width,
        Height = height,
        DpiScale = scale,
        PngData = Array.Empty<byte>()
    };

    private static SourceWindowInfo Window(nint handle, SourceWindowBounds bounds) =>
        new(handle, checked((uint)handle), $"窗口 {handle}", bounds);
}
