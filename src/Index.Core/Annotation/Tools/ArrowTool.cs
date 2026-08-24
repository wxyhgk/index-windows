using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class ArrowTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Arrow;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.Width };
    public override bool IsPinnedToBar => true;
    public override bool UsesEndpointHandles => true;
    public override ResizeHandle[] ResizeHandles => new[] { ResizeHandle.TopLeft, ResizeHandle.BottomRight };

    public override PointF HandleLocation(ResizeHandle handle, Layer layer)
    {
        var rect = layer.Rect;
        return handle == ResizeHandle.TopLeft
            ? new PointF(rect.X, rect.Y)
            : new PointF(rect.X + rect.W, rect.Y + rect.H);
    }

    public override bool HitTest(Layer layer, PointF point, double tolerance)
    {
        var from = new PointF(layer.Rect.X, layer.Rect.Y);
        var to = new PointF(layer.Rect.X + layer.Rect.W, layer.Rect.Y + layer.Rect.H);
        return ToolGeometry.DistanceToSegment(point, from, to) <= tolerance;
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
        double length = Math.Sqrt(rect.W * rect.W + rect.H * rect.H);
        return length >= ToolGeometry.MinimumSide;
    }

    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        var from = new PointF(layer.Rect.X, layer.Rect.Y);
        var to = new PointF(layer.Rect.X + layer.Rect.W, layer.Rect.Y + layer.Rect.H);

        using var paint = new SKPaint
        {
            Color = layer.Color.ToSKColor(),
            StrokeWidth = (float)lineWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        // 线段
        canvas.DrawLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y, paint);

        // 箭头
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        double headLength = Math.Max(lineWidth * 4, 12);
        double headAngle = Math.PI / 7;

        var headLeft = new PointF(
            to.X - headLength * Math.Cos(angle - headAngle),
            to.Y - headLength * Math.Sin(angle - headAngle));
        var headRight = new PointF(
            to.X - headLength * Math.Cos(angle + headAngle),
            to.Y - headLength * Math.Sin(angle + headAngle));

        using var path = new SKPath();
        path.MoveTo((float)to.X, (float)to.Y);
        path.LineTo((float)headLeft.X, (float)headLeft.Y);
        path.MoveTo((float)to.X, (float)to.Y);
        path.LineTo((float)headRight.X, (float)headRight.Y);
        canvas.DrawPath(path, paint);
    }
}
