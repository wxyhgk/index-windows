using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class HighlightTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Highlight;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.Opacity };
    public override ToolStyle DefaultStyle => new() { ColorIndex = 1 };
    public override bool IsPinnedToBar => true;

    public override Layer MakeLayer(ToolLayerContext context)
    {
        var layer = base.MakeLayer(context);
        // 高亮用半透明填充
        var c = context.Color;
        layer.Color = new LColor(c.R, c.G, c.B, context.Style.ValueFor(ToolStyleAxis.Opacity));
        return layer;
    }

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var rect = layer.Rect.Standardized();
        using var paint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.Multiply
        };
        canvas.DrawRect(new SKRect(
            (float)rect.MinX,
            (float)rect.MinY,
            (float)rect.MaxX,
            (float)rect.MaxY), paint);
    }
}
