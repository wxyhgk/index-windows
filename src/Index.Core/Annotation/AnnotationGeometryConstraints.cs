namespace Index.Annotation;

/// <summary>
/// 绘制期间的纯几何约束。输入层只负责传递修饰键状态，具体形状规则留在标注领域。
/// </summary>
public static class AnnotationGeometryConstraints
{
    private const double AngleStep = Math.PI / 4;

    public static PointF ConstrainEndpoint(AnnotationTool tool, PointF from, PointF to)
    {
        return tool switch
        {
            AnnotationTool.Rect or AnnotationTool.Ellipse => ConstrainSquare(from, to),
            AnnotationTool.Line or AnnotationTool.Arrow => ConstrainAngle(from, to),
            _ => to
        };
    }

    private static PointF ConstrainSquare(PointF from, PointF to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double side = Math.Max(Math.Abs(dx), Math.Abs(dy));

        return new PointF(
            from.X + Math.CopySign(side, dx == 0 ? 1 : dx),
            from.Y + Math.CopySign(side, dy == 0 ? 1 : dy));
    }

    private static PointF ConstrainAngle(PointF from, PointF to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0) return to;

        double angle = Math.Atan2(dy, dx);
        double snappedAngle = Math.Round(angle / AngleStep, MidpointRounding.AwayFromZero) * AngleStep;
        return new PointF(
            from.X + length * Math.Cos(snappedAngle),
            from.Y + length * Math.Sin(snappedAngle));
    }
}
