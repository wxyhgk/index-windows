using Index.Capture;
using Index.Platform;

namespace Index.Tests;

public sealed class WindowTargetNavigatorTests
{
    private static readonly CaptureDisplayIdentity Display =
        new("display-1", 100, 200, 1000, 800, 1);
    private static readonly CaptureCoordinateMapper Coordinates =
        new(1000, 800, 1000, 800, 1);

    [Fact]
    public void PrefersTopLevelWindowAndCyclesThroughFrozenCandidates()
    {
        var child = Target(2, new SelectionRect(20, 20, 100, 100), depth: 1);
        var topLevel = Target(1, new SelectionRect(0, 0, 200, 200), depth: 0);
        var navigator = new WindowTargetNavigator(new[] { child, topLevel }, null, Display);
        var point = new SelectionPoint(50, 50);

        Assert.Equal(topLevel, navigator.PreviewAt(point, Coordinates));
        Assert.False(navigator.IsCurrentTargetExplicit);
        Assert.Equal(2, navigator.CandidateCount);
        Assert.True(navigator.TryCycle(point, 1, Coordinates, out var next));
        Assert.Equal(child, next);
        Assert.True(navigator.IsCurrentTargetExplicit);
        Assert.True(navigator.TryCycle(point, -1, Coordinates, out var previous));
        Assert.Equal(topLevel, previous);
        Assert.True(navigator.IsCurrentTargetExplicit);
    }

    [Fact]
    public void ResetsCandidateStateWhenPointerMovesToAnotherWindow()
    {
        var first = Target(1, new SelectionRect(0, 0, 100, 100));
        var second = Target(2, new SelectionRect(200, 0, 100, 100));
        var navigator = new WindowTargetNavigator(new[] { first, second }, null, Display);

        Assert.Equal(first, navigator.PreviewAt(new SelectionPoint(50, 50), Coordinates));
        Assert.Equal(second, navigator.PreviewAt(new SelectionPoint(250, 50), Coordinates));
        Assert.False(navigator.IsCurrentTargetExplicit);

        navigator.Reset();
        Assert.Null(navigator.CurrentTarget);
        Assert.False(navigator.IsCurrentTargetExplicit);
        Assert.Equal(0, navigator.CandidateCount);
    }

    [Fact]
    public void ExplicitCyclePersistsOnlyWhileTheCandidateSetIsUnchanged()
    {
        var child = Target(2, new SelectionRect(20, 20, 100, 100), depth: 1);
        var topLevel = Target(1, new SelectionRect(0, 0, 200, 200));
        var navigator = new WindowTargetNavigator([child, topLevel], null, Display);

        Assert.Equal(topLevel, navigator.PreviewAt(new SelectionPoint(50, 50), Coordinates));
        Assert.True(navigator.TryCycle(
            new SelectionPoint(50, 50),
            1,
            Coordinates,
            out var selectedChild));
        Assert.Equal(child, selectedChild);
        Assert.Equal(child, navigator.PreviewAt(new SelectionPoint(80, 80), Coordinates));
        Assert.True(navigator.IsCurrentTargetExplicit);

        Assert.Equal(topLevel, navigator.PreviewAt(new SelectionPoint(150, 150), Coordinates));
        Assert.False(navigator.IsCurrentTargetExplicit);
    }

    [Fact]
    public void AppendsPixelRegionAfterRegularWindow()
    {
        var pixels = new byte[100 * 80];
        Array.Fill(pixels, (byte)10);
        for (int y = 10; y < 70; y++)
            Array.Fill(pixels, (byte)200, y * 100 + 20, 60);
        var detector = new FrozenPixelEdgeDetector(
            new LuminanceBuffer(100, 80, 100, pixels),
            new FrozenPixelEdgeOptions { MinimumWidth = 10, MinimumHeight = 10 });
        var regular = Target(1, new SelectionRect(0, 0, 100, 80));
        var coordinates = new CaptureCoordinateMapper(100, 80, 100, 80, 1);
        var navigator = new WindowTargetNavigator(new[] { regular }, detector, Display);
        var point = new SelectionPoint(50, 40);

        Assert.Equal(regular, navigator.PreviewAt(point, coordinates));
        Assert.Equal(2, navigator.CandidateCount);
        Assert.True(navigator.TryCycle(point, 1, coordinates, out var pixelTarget));
        Assert.True(pixelTarget?.IsPixelRegion);
        Assert.Equal(new SelectionRect(20, 10, 60, 60), pixelTarget?.Bounds);
    }

    [Fact]
    public void AddsPixelRegionWhenDetectorArrivesAfterFirstPreview()
    {
        var regular = Target(1, new SelectionRect(0, 0, 100, 80));
        var coordinates = new CaptureCoordinateMapper(100, 80, 100, 80, 1);
        var navigator = new WindowTargetNavigator([regular], null, Display);
        var point = new SelectionPoint(50, 40);

        Assert.Equal(regular, navigator.PreviewAt(point, coordinates));
        Assert.Equal(1, navigator.CandidateCount);

        var pixels = new byte[100 * 80];
        Array.Fill(pixels, (byte)10);
        for (int y = 10; y < 70; y++)
            Array.Fill(pixels, (byte)200, y * 100 + 20, 60);
        navigator.SetPixelEdgeDetector(new FrozenPixelEdgeDetector(
            new LuminanceBuffer(100, 80, 100, pixels),
            new FrozenPixelEdgeOptions { MinimumWidth = 10, MinimumHeight = 10 }));

        Assert.Null(navigator.CurrentTarget);
        Assert.False(navigator.IsCurrentTargetExplicit);
        Assert.Equal(regular, navigator.PreviewAt(point, coordinates));
        Assert.Equal(2, navigator.CandidateCount);
    }

    [Fact]
    public void ExplicitWindowTargetStaysBoundWhenSelectionCenterOverlapsAnotherWindow()
    {
        var selected = Target(10, new SelectionRect(0, 0, 300, 300));
        var other = Target(20, new SelectionRect(100, 100, 300, 300));
        var navigator = new WindowTargetNavigator([other, selected], null, Display);
        var selection = new SelectionRect(150, 150, 100, 100);

        Assert.Equal((nint)10, navigator.ResolveCaptureHandle(selected, selection, Coordinates));
        Assert.Equal((nint)20, navigator.ResolveCaptureHandle(null, selection, Coordinates));
    }

    [Fact]
    public void NestedTargetResolvesToItsFrozenRootHandle()
    {
        var child = Target(
            12,
            new SelectionRect(20, 20, 100, 100),
            depth: 2,
            rootHandle: 10);
        var navigator = new WindowTargetNavigator([child], null, Display);

        Assert.Equal(
            (nint)10,
            navigator.ResolveCaptureHandle(
                child,
                new SelectionRect(20, 20, 100, 100),
                Coordinates));
    }

    [Fact]
    public void ExplicitPixelRegionDoesNotBecomeAWindowCapture()
    {
        var window = Target(10, new SelectionRect(0, 0, 300, 300));
        var pixel = Target(0, new SelectionRect(20, 20, 100, 100), depth: int.MaxValue);
        var navigator = new WindowTargetNavigator([window], null, Display);

        Assert.Equal(
            nint.Zero,
            navigator.ResolveCaptureHandle(
                pixel,
                pixel.Bounds,
                Coordinates));
    }

    private static WindowSelectionTarget Target(
        nint handle,
        SelectionRect bounds,
        int depth = 0,
        nint rootHandle = default) =>
        new(handle, new SourceWindowBounds(0, 0, 100, 100), bounds, depth, rootHandle);
}
