using Index.Annotation;
using Index.Annotation.Tools;
using SkiaSharp;

namespace Index.Tests;

public sealed class ToolRenderingTests
{
    [Fact]
    public void RectangleRenderer_UsesRightAndBottomAsEdges_NotWidthAndHeight()
    {
        using var bitmap = new SKBitmap(120, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var layer = new Layer(
            LayerKind.Rect,
            new LRect(10, 20, 60, 40),
            LColor.Red,
            2);

        new RectTool().Draw(layer, canvas, 2, 12);

        Assert.NotEqual(0, bitmap.GetPixel(10, 40).Alpha);
        Assert.NotEqual(0, bitmap.GetPixel(70, 40).Alpha);
        Assert.Equal(0, bitmap.GetPixel(80, 40).Alpha);
    }

    [Fact]
    public void EllipseRenderer_StaysInsideLayerBounds()
    {
        using var bitmap = new SKBitmap(120, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var layer = new Layer(
            LayerKind.Ellipse,
            new LRect(10, 20, 60, 40),
            LColor.Red,
            2);

        new EllipseTool().Draw(layer, canvas, 2, 12);

        Assert.NotEqual(0, bitmap.GetPixel(40, 20).Alpha);
        Assert.NotEqual(0, bitmap.GetPixel(70, 40).Alpha);
        Assert.Equal(0, bitmap.GetPixel(85, 40).Alpha);
    }
}
