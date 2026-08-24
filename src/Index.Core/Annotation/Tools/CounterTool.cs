using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class CounterTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Counter;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.FontSize };
    public override ToolInput Input => ToolInput.Click;
    public override bool IsPinnedToBar => true;

    public override Layer MakeLayer(ToolLayerContext context)
    {
        // 删除中间序号后不重排，新编号始终从现存最大值继续。
        int next = context.Existing
            .Where(layer => layer.Kind == LayerKind.Counter)
            .Select(layer => int.TryParse(layer.Text, out int value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var layer = base.MakeLayer(context);
        double diameter = Math.Max(28 * context.StrokeScale, layer.FontSize * 1.4);
        layer.Rect = new LRect(
            context.From.X - diameter / 2,
            context.From.Y - diameter / 2,
            diameter,
            diameter);
        layer.Text = next.ToString();
        return layer;
    }

    public override bool HitTest(Layer layer, PointF point, double tolerance)
    {
        var rect = layer.Rect.Standardized();
        var center = new PointF(rect.MidX, rect.MidY);
        double dx = point.X - center.X;
        double dy = point.Y - center.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= Math.Min(rect.W, rect.H) / 2 + tolerance;
    }

    public override Layer Resize(
        Layer original,
        ResizeHandle handle,
        PointF delta,
        double pixelScale)
    {
        return original with { Rect = SquareAfterResize(handle, delta, original.Rect) };
    }

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var rect = layer.Rect.Standardized();
        if (rect.W < 1 || rect.H < 1) return;

        float cx = (float)rect.MidX;
        float cy = (float)rect.MidY;
        float radius = (float)(Math.Min(rect.W, rect.H) / 2);

        using var fillPaint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(cx, cy, radius, fillPaint);

        if (string.IsNullOrEmpty(layer.Text)) return;

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var font = new SKFont(typeface, (float)(rect.H * 0.52));
        var metrics = font.Metrics;
        float baseline = cy - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(layer.Text, cx, baseline, SKTextAlign.Center, font, textPaint);
    }

    internal static LRect SquareAfterResize(
        ResizeHandle handle,
        PointF delta,
        LRect original)
    {
        var source = original.Standardized();
        if (handle == ResizeHandle.Inside)
            return source.OffsetBy(delta.X, delta.Y);

        bool movesLeft = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        bool movesRight = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        bool movesTop = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        bool movesBottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

        double draggedX = movesLeft ? source.MinX + delta.X : source.MaxX + delta.X;
        double fixedX = movesLeft ? source.MaxX : source.MinX;
        double draggedY = movesTop ? source.MinY + delta.Y : source.MaxY + delta.Y;
        double fixedY = movesTop ? source.MaxY : source.MinY;

        double diameter = handle switch
        {
            ResizeHandle.Top or ResizeHandle.Bottom => Math.Abs(draggedY - fixedY),
            ResizeHandle.Left or ResizeHandle.Right => Math.Abs(draggedX - fixedX),
            _ => Math.Max(Math.Abs(draggedX - fixedX), Math.Abs(draggedY - fixedY))
        };

        double x = movesLeft || movesRight
            ? (draggedX < fixedX ? fixedX - diameter : fixedX)
            : source.MidX - diameter / 2;
        double y = movesTop || movesBottom
            ? (draggedY < fixedY ? fixedY - diameter : fixedY)
            : source.MidY - diameter / 2;
        return new LRect(x, y, diameter, diameter);
    }
}
