using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class RectTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Rect;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.Width };
    public override bool IsPinnedToBar => true;

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var rect = layer.Rect.Standardized();
        using var paint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            StrokeWidth = (float)lineWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(
            (float)rect.MinX,
            (float)rect.MinY,
            (float)rect.MaxX,
            (float)rect.MaxY), paint);
    }
}
