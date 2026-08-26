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
        Assert.Equal(2, navigator.CandidateCount);
        Assert.True(navigator.TryCycle(point, 1, Coordinates, out var next));
        Assert.Equal(child, next);
        Assert.True(navigator.TryCycle(point, -1, Coordinates, out var previous));
        Assert.Equal(topLevel, previous);
    }

    [Fact]
    public void ResetsCandidateStateWhenPointerMovesToAnotherWindow()
    {
        var first = Target(1, new SelectionRect(0, 0, 100, 100));
        var second = Target(2, new SelectionRect(200, 0, 100, 100));
        var navigator = new WindowTargetNavigator(new[] { first, second }, null, Display);

        Assert.Equal(first, navigator.PreviewAt(new SelectionPoint(50, 50), Coordinates));
        Assert.Equal(second, navigator.PreviewAt(new SelectionPoint(250, 50), Coordinates));

        navigator.Reset();
        Assert.Null(navigator.CurrentTarget);
        Assert.Equal(0, navigator.CandidateCount);
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
        Assert.Equal(regular, navigator.PreviewAt(point, coordinates));
        Assert.Equal(2, navigator.CandidateCount);
    }

    private static WindowSelectionTarget Target(nint handle, SelectionRect bounds, int depth = 0) =>
        new(handle, new SourceWindowBounds(0, 0, 100, 100), bounds, depth);
}
