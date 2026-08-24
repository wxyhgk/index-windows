using Index.Capture;

namespace Index.Tests;

public sealed class CaptureCoordinateMapperTests
{
    [Fact]
    public void UsesIndependentAxisScaleForCropAndDisplaySize()
    {
        var mapper = new CaptureCoordinateMapper(3000, 2000, 1500, 800, 1.25);
        var selection = new SelectionRect(10.9, 20.9, 100.4, 50.4);

        Assert.Equal(new PixelCaptureRect(21, 52, 200, 126), mapper.ToCropRect(selection));
        Assert.Equal(new PixelCaptureSize(201, 126), mapper.ToDisplaySize(selection));
    }

    [Fact]
    public void RoundTripsPixelAndLogicalCoordinates()
    {
        var mapper = new CaptureCoordinateMapper(2400, 1350, 1920, 1080, 1);
        var pixels = new SelectionRect(125, 250, 500, 375);

        var logical = mapper.PixelToLogical(pixels);

        Assert.Equal(pixels.X, mapper.LogicalToPixel(new SelectionPoint(logical.X, logical.Y)).X, 8);
        Assert.Equal(pixels.Y, mapper.LogicalToPixel(new SelectionPoint(logical.X, logical.Y)).Y, 8);
        Assert.Equal(pixels.Width, logical.Width * mapper.ScaleX, 8);
        Assert.Equal(pixels.Height, logical.Height * mapper.ScaleY, 8);
    }

    [Fact]
    public void UsesDpiFallbackBeforeLogicalSurfaceIsMeasured()
    {
        var mapper = new CaptureCoordinateMapper(2000, 1000, 0, 0, 1.5);

        Assert.Equal(1.5, mapper.ScaleX);
        Assert.Equal(1.5, mapper.ScaleY);
    }
}
