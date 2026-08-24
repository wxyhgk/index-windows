using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class CropTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Crop;
    public override ToolStyleAxis[] Axes => Array.Empty<ToolStyleAxis>();

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var rect = layer.Rect.Standardized();
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 128),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        // 裁剪框：白色虚线
        paint.PathEffect = SKPathEffect.CreateDash(new float[] { 8, 4 }, 0);
        canvas.DrawRect(new SKRect(
            (float)rect.MinX,
            (float)rect.MinY,
            (float)rect.MaxX,
            (float)rect.MaxY), paint);
        paint.PathEffect = null;
    }
}
