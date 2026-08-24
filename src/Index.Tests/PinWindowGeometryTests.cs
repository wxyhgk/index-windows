using Index.Pin;

namespace Index.Tests;

public sealed class PinWindowGeometryTests
{
    private static readonly PinRect WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void InitialFrame_PreservesNaturalSizeAndAnchorWhenItFits()
    {
        var result = PinWindowGeometry.InitialFrame(new PinRect(200, 150, 640, 480), WorkArea);

        Assert.Equal(new PinRect(200, 150, 640, 480), result);
    }

    [Fact]
    public void InitialFrame_FitsOversizedImageWithoutChangingAspectRatio()
    {
        var result = PinWindowGeometry.InitialFrame(new PinRect(0, 0, 4000, 2000), WorkArea);

        Assert.True(result.Width <= WorkArea.Width);
        Assert.True(result.Height <= WorkArea.Height);
        Assert.Equal(2, result.Width / result.Height, precision: 6);
    }

    [Fact]
    public void ZoomAroundCenter_PreservesAspectRatioAndCenter()
    {
        var current = new PinRect(400, 300, 600, 300);
        var result = PinWindowGeometry.ZoomAroundCenter(current, 600, 300, 1.5, WorkArea);

        Assert.Equal(current.MidX, result.MidX, precision: 6);
        Assert.Equal(current.MidY, result.MidY, precision: 6);
        Assert.Equal(900, result.Width);
        Assert.Equal(450, result.Height);
    }

    [Fact]
    public void ZoomAroundCenter_ClampsZoomAndKeepsWindowReachable()
    {
        var current = new PinRect(1800, 1000, 100, 50);
        var result = PinWindowGeometry.ZoomAroundCenter(current, 100, 50, 100, WorkArea);

        Assert.Equal(400, result.Width);
        Assert.Equal(200, result.Height);
        Assert.True(result.X + result.Width <= WorkArea.X + WorkArea.Width);
        Assert.True(result.Y + result.Height <= WorkArea.Y + WorkArea.Height);
    }

    [Fact]
    public void ToolbarIsPlacedBelowImageWhenThereIsRoom()
    {
        var result = PinToolbarGeometry.Place(
            new PinRect(400, 200, 600, 400),
            toolbarWidth: 120,
            toolbarHeight: 34,
            WorkArea);

        Assert.Equal(640, result.X);
        Assert.Equal(608, result.Y);
    }

    [Fact]
    public void ToolbarFlipsAboveImageNearBottomEdge()
    {
        var result = PinToolbarGeometry.Place(
            new PinRect(400, 800, 600, 250),
            toolbarWidth: 120,
            toolbarHeight: 34,
            WorkArea);

        Assert.Equal(758, result.Y);
        Assert.True(result.X >= WorkArea.X);
        Assert.True(result.X + result.Width <= WorkArea.X + WorkArea.Width);
    }

    [Fact]
    public void HighlightFrame_ExpandsOutsideImageAndRoundTrips()
    {
        var image = new PinRect(100, 80, 640, 480);

        var outer = PinHighlight.OuterFrame(image);

        Assert.Equal(new PinRect(98, 78, 644, 484), outer);
        Assert.Equal(image, PinHighlight.ImageFrame(outer));
    }

    [Fact]
    public void HighlightPixels_PreserveImageAndAddOpaqueAccentBorder()
    {
        byte[] image =
        [
            1, 2, 3, 255,
            4, 5, 6, 255
        ];

        byte[] result = PinHighlight.ComposePremultipliedBgra(image, 2, 1);
        int outerWidth = 2 + PinHighlight.Thickness * 2;
        int imageOffset = (PinHighlight.Thickness * outerWidth + PinHighlight.Thickness) * 4;

        Assert.Equal(outerWidth * (1 + PinHighlight.Thickness * 2) * 4, result.Length);
        Assert.Equal(new byte[] { 0xFF, 0xA3, 0x69, 0xFF }, result[..4]);
        Assert.Equal(image, result[imageOffset..(imageOffset + image.Length)]);
    }

    [Fact]
    public void InteractionState_TracksZoomWithoutKnowingAboutAWindow()
    {
        var state = new PinInteractionState(600, 300);
        state.Initialize(new PinRect(400, 300, 600, 300));

        var result = state.ApplyZoom(
            new PinRect(400, 300, 600, 300),
            state.SteppedZoom(120),
            WorkArea);

        Assert.Equal(1.1, state.Zoom, precision: 6);
        Assert.Equal(660, result.Width, precision: 6);
        Assert.Equal(330, result.Height, precision: 6);
        Assert.Equal(state.Zoom, state.SteppedZoom(0));
    }

    [Fact]
    public void InteractionState_ClampsOpacityAndProducesNativeAlpha()
    {
        var state = new PinInteractionState(100, 100);

        for (int i = 0; i < 20; i++)
            state.StepOpacity(-120);

        Assert.Equal(0.2, state.Opacity, precision: 6);
        Assert.Equal(51, state.OpacityByte);
    }
}
