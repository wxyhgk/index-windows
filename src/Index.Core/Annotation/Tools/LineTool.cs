using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class LineTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Line;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.Width };
    public override bool IsPinnedToBar => false;
    public override bool UsesEndpointHandles => true;
    public override ResizeHandle[] ResizeHandles => new[] { ResizeHandle.TopLeft, ResizeHandle.BottomRight };

    public override PointF HandleLocation(ResizeHandle handle, Layer layer)
    {
        var r = layer.Rect;
        return handle switch
        {
            ResizeHandle.TopLeft => new PointF(r.X, r.Y),
            ResizeHandle.BottomRight => new PointF(r.X + r.W, r.Y + r.H),
            _ => base.HandleLocation(handle, layer)
        };
    }

    public override bool HitTest(Layer layer, PointF point, double tolerance)
    {
        var r = layer.Rect;
        var a = new PointF(r.X, r.Y);
        var b = new PointF(r.X + r.W, r.Y + r.H);
        return ToolGeometry.DistanceToSegment(point, a, b)
            <= tolerance + Math.Max(0, layer.LineWidth) / 2;
    }

    public override Layer Resize(Layer original, ResizeHandle handle, PointF delta, double pixelScale)
    {
        var from = new PointF(original.Rect.X, original.Rect.Y);
        var to = new PointF(original.Rect.X + original.Rect.W, original.Rect.Y + original.Rect.H);

        if (handle == ResizeHandle.TopLeft)
            from = from + delta;
        else
            to = to + delta;

        return original with { Rect = new LRect(from, to) };
    }

    public override bool MeetsMinimumSize(Layer layer)
    {
        var rect = layer.Rect;
        return Math.Max(Math.Abs(rect.W), Math.Abs(rect.H)) >= ToolGeometry.MinimumSide;
    }

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var r = layer.Rect;
        using var paint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            StrokeWidth = (float)lineWidth,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawLine((float)r.X, (float)r.Y, (float)(r.X + r.W), (float)(r.Y + r.H), paint);
    }
}
