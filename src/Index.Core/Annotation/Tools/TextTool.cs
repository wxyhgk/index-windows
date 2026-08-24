using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class TextTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Text;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.FontSize };
    public override ToolInput Input => ToolInput.Click;
    public override bool IsPinnedToBar => true;

    public override Layer MakeLayer(ToolLayerContext context)
    {
        var layer = base.MakeLayer(context);
        // 文字层 Rect 只存锚点（w/h 恒为 0）
        return layer with
        {
            Rect = new LRect(context.From.X, context.From.Y, 0, 0),
            FontSize = context.Style.ValueFor(ToolStyleAxis.FontSize) * context.StrokeScale
        };
    }

    public override bool HitTest(Layer layer, PointF point, double tolerance)
        => layer.HandleBounds.InsetBy(-tolerance, -tolerance).Contains(point);

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        if (string.IsNullOrEmpty(layer.Text)) return;

        using var paint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            TextSize = (float)fontSize,
            IsAntialias = true
        };
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));
        paint.Typeface = typeface;

        canvas.DrawText(layer.Text, (float)layer.Rect.X, (float)(layer.Rect.Y + fontSize), paint);
    }
}
