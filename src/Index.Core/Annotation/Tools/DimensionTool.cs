using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class DimensionTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Dimension;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.Width };

    public override Layer MakeLayer(ToolLayerContext context)
    {
        var layer = base.MakeLayer(context);
        // 测量值在生成时烤进 text：宽×高（图像像素）
        var rect = layer.Rect.Standardized();
        int pixelW = (int)(rect.W * context.PixelScale);
        int pixelH = (int)(rect.H * context.PixelScale);
        layer.Text = $"{pixelW} × {pixelH}";
        layer.FontSize = 14;
        return layer;
    }

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var rect = layer.Rect.Standardized();
        using var paint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            StrokeWidth = (float)lineWidth,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        // 矩形框
        canvas.DrawRect(new SKRect(
            (float)rect.MinX,
            (float)rect.MinY,
            (float)rect.MaxX,
            (float)rect.MaxY), paint);

        // 尺寸标注
        if (!string.IsNullOrEmpty(layer.Text))
        {
            using var textPaint = new SKPaint
            {
                Color = layer.Color.ToSKColor(),
                TextSize = (float)fontSize,
                IsAntialias = true
            };
            float textWidth = textPaint.MeasureText(layer.Text);
            float textX = (float)rect.MidX - textWidth / 2;
            float textY = (float)(rect.MaxY + fontSize + 4);
            canvas.DrawText(layer.Text, textX, textY, textPaint);
        }
    }
}
