using Index.Annotation;
using Index.Capture;
using Index.Platform;

namespace Index.Tests;

public sealed class HighResolutionCaptureGeometryTests
{
    [Fact]
    public void MapsFirstSelectionDirectlyIntoDenseWindowFrame()
    {
        var mapping = HighResolutionCaptureGeometry.MapFromOriginalWindow(
            new SourceWindowBounds(400, 400, 1000, 800),
            new SourceWindowBounds(100, 200, 1300, 1000),
            2400,
            1600);

        Assert.Equal(new SourceWindowBounds(400, 400, 1000, 800), mapping.GlobalBounds);
        Assert.Equal(new SourceWindowBounds(600, 400, 1800, 1200), mapping.ImageBounds);
        Assert.Equal(2, mapping.ScaleX);
        Assert.Equal(2, mapping.ScaleY);
    }

    [Fact]
    public void ClipsFirstSelectionToCapturedWindow()
    {
        var mapping = HighResolutionCaptureGeometry.MapFromOriginalWindow(
            new SourceWindowBounds(0, 100, 700, 500),
            new SourceWindowBounds(100, 200, 1300, 1000),
            2400,
            1600);

        Assert.Equal(new SourceWindowBounds(100, 200, 700, 500), mapping.GlobalBounds);
        Assert.Equal(new SourceWindowBounds(0, 0, 1200, 600), mapping.ImageBounds);
    }

    [Fact]
    public void ScalesExistingAnnotationsIntoDenseSelection()
    {
        var sourceLayers = new Layers<ImageSpace>();
        sourceLayers.Append(new Layer(
            LayerKind.Rect,
            new LRect(150, 120, 200, 100),
            new LColor(1, 0, 0, 1),
            3,
            fontSize: 12));
        var requested = new SourceWindowBounds(0, 100, 700, 500);
        var mapping = HighResolutionCaptureGeometry.MapFromOriginalWindow(
            requested,
            new SourceWindowBounds(100, 200, 1300, 1000),
            2400,
            1600);

        var mapped = HighResolutionCaptureGeometry.MapLayersFromOriginalSelection(
            sourceLayers,
            requested,
            mapping);

        var layer = Assert.Single(mapped.Elements);
        Assert.Equal(new LRect(100, 40, 400, 200), layer.Rect);
        Assert.Equal(6, layer.LineWidth);
        Assert.Equal(24, layer.FontSize);
    }

    [Fact]
    public void RejectsSelectionOutsideCapturedWindow()
    {
        Assert.Throws<InvalidOperationException>(() =>
            HighResolutionCaptureGeometry.MapFromOriginalWindow(
                new SourceWindowBounds(0, 0, 50, 50),
                new SourceWindowBounds(100, 200, 1300, 1000),
                2400,
                1600));
    }
}
