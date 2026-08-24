using Index.Annotation;
using Index.Annotation.Tools;
using SkiaSharp;

namespace Index.Tests;

public sealed class CommonAnnotationToolTests
{
    [Fact]
    public void Highlight_UsesOrangeDefaultAndMultiplyBlendForReverseDrag()
    {
        var tool = new HighlightTool();
        Assert.Equal(1, tool.DefaultStyle.ColorIndex);

        using var bitmap = new SKBitmap(12, 12, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        var background = new SKColor(200, 160, 120);
        canvas.Clear(background);
        var layer = new Layer(
            LayerKind.Highlight,
            new LRect(8, 8, -6, -6),
            new LColor(1, 128d / 255, 0, 0.5),
            0);

        tool.Draw(layer, canvas, 0, 0);

        var inside = bitmap.GetPixel(3, 3);
        Assert.InRange(inside.Red, (byte)197, (byte)202);
        Assert.InRange(inside.Green, (byte)117, (byte)123);
        Assert.InRange(inside.Blue, (byte)57, (byte)63);
        // SKCanvas.DrawRect(x, y, width, height) would incorrectly extend this
        // reverse-drag rectangle to x=10; the normalized SKRect must stop at x=8.
        Assert.Equal(background, bitmap.GetPixel(9, 3));
    }

    [Fact]
    public void Line_HitTestIncludesHalfStrokeWidth()
    {
        var tool = new LineTool();
        var layer = new Layer(
            LayerKind.Line,
            new LRect(2, 5, 20, 0),
            LColor.Red,
            10);

        Assert.True(tool.HitTest(layer, new PointF(12, 9.5), 0));
        Assert.False(tool.HitTest(layer, new PointF(12, 10.5), 0));
    }

    [Fact]
    public void Line_ReverseEndpointsSurviveResizeAndMinimumUsesDominantAxis()
    {
        var tool = new LineTool();
        var reverse = new Layer(
            LayerKind.Line,
            new LRect(10, 10, -6, -4),
            LColor.Red,
            2);

        var resized = tool.Resize(
            reverse,
            ResizeHandle.TopLeft,
            new PointF(2, 3),
            pixelScale: 1);

        Assert.Equal(new PointF(12, 13), tool.HandleLocation(ResizeHandle.TopLeft, resized));
        Assert.Equal(new PointF(4, 6), tool.HandleLocation(ResizeHandle.BottomRight, resized));
        Assert.False(tool.MeetsMinimumSize(new Layer(
            LayerKind.Line,
            new LRect(0, 0, 3, 3),
            LColor.Red,
            1)));
        Assert.True(tool.MeetsMinimumSize(new Layer(
            LayerKind.Line,
            new LRect(0, 0, -4, 0),
            LColor.Red,
            1)));
    }

    [Fact]
    public void Counter_MakeLayerUsesStyledDiameterCenterAndMaxExistingNumber()
    {
        var existing = new[]
        {
            CounterLayer("2"),
            CounterLayer("7"),
            CounterLayer("not-a-number")
        };
        var style = new ToolStyle { FontSizeIndex = 2 };
        var tool = new CounterTool();

        var layer = tool.MakeLayer(new ToolLayerContext
        {
            From = new PointF(100, 100),
            To = new PointF(100, 100),
            Style = style,
            Color = LColor.Red,
            StrokeScale = 1,
            PixelScale = 1,
            Existing = existing
        });

        Assert.Contains(ToolStyleAxis.FontSize, tool.Axes);
        Assert.Equal("8", layer.Text);
        Assert.Equal(40, layer.FontSize);
        Assert.Equal(new LRect(72, 72, 56, 56), layer.Rect);
    }

    [Fact]
    public void Counter_HitResizeAndDrawFollowActualCircularBounds()
    {
        var tool = new CounterTool();
        var layer = new Layer(
            LayerKind.Counter,
            new LRect(10, 20, 30, 30),
            new LColor(1, 0, 0),
            0,
            "12",
            24);

        Assert.True(tool.HitTest(layer, new PointF(25, 35), 0));
        Assert.False(tool.HitTest(layer, new PointF(10, 20), 0));

        var resized = tool.Resize(layer, ResizeHandle.Right, new PointF(10, 0), 1);
        Assert.Equal(new LRect(10, 15, 40, 40), resized.Rect);

        var crossed = tool.Resize(layer, ResizeHandle.Right, new PointF(-50, 0), 1);
        Assert.Equal(new LRect(-10, 25, 20, 20), crossed.Rect);

        using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        tool.Draw(resized, canvas, resized.LineWidth, resized.FontSize);

        Assert.Equal((byte)0, bitmap.GetPixel(10, 15).Alpha);
        Assert.True(bitmap.GetPixel(12, 35).Alpha > 0);
        Assert.Equal((byte)0, bitmap.GetPixel(51, 35).Alpha);
    }

    private static Layer CounterLayer(string text)
        => new(
            LayerKind.Counter,
            new LRect(0, 0, 28, 28),
            LColor.Red,
            0,
            text,
            20);
}
