namespace Index.Annotation;

/// <summary>
/// 可编码矩形。图像像素坐标系，原点左上角。
/// 对应 macOS 端 LRect。
/// </summary>
public sealed record LRect
{
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }

    public LRect(double x, double y, double w, double h)
    {
        X = x; Y = y; W = w; H = h;
    }

    public LRect(PointF from, PointF to)
        : this(from.X, from.Y, to.X - from.X, to.Y - from.Y) { }

    /// <summary>标准化：负宽高翻转为正。</summary>
    public LRect Standardized()
    {
        double x = Math.Min(X, X + W);
        double y = Math.Min(Y, Y + H);
        return new LRect(x, y, Math.Abs(W), Math.Abs(H));
    }

    public double MinX => Math.Min(X, X + W);
    public double MaxX => Math.Max(X, X + W);
    public double MinY => Math.Min(Y, Y + H);
    public double MaxY => Math.Max(Y, Y + H);
    public double MidX => X + W / 2;
    public double MidY => Y + H / 2;

    public bool Contains(PointF p)
    {
        return p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }

    public LRect OffsetBy(double dx, double dy) => new(X + dx, Y + dy, W, H);

    public LRect InsetBy(double dx, double dy)
        => new(X + dx, Y + dy, W - dx * 2, H - dy * 2);

    /// <summary>Intersect this rectangle with a canvas whose origin is (0, 0).</summary>
    public LRect IntersectedWithBounds(double width, double height)
    {
        double safeWidth = Math.Max(0, width);
        double safeHeight = Math.Max(0, height);
        double left = Math.Clamp(MinX, 0, safeWidth);
        double top = Math.Clamp(MinY, 0, safeHeight);
        double right = Math.Clamp(MaxX, 0, safeWidth);
        double bottom = Math.Clamp(MaxY, 0, safeHeight);
        return new LRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>Move the rectangle inside a canvas while preserving its size when possible.</summary>
    public LRect PositionedWithinBounds(double width, double height)
    {
        var rect = Standardized();
        double safeWidth = Math.Max(0, width);
        double safeHeight = Math.Max(0, height);
        double constrainedWidth = Math.Min(rect.W, safeWidth);
        double constrainedHeight = Math.Min(rect.H, safeHeight);
        double x = Math.Clamp(rect.X, 0, Math.Max(0, safeWidth - constrainedWidth));
        double y = Math.Clamp(rect.Y, 0, Math.Max(0, safeHeight - constrainedHeight));
        return new LRect(x, y, constrainedWidth, constrainedHeight);
    }

    public override string ToString() => $"LRect({X:F1}, {Y:F1}, {W:F1}×{H:F1})";
}

/// <summary>
/// 画布空间点（左上原点、Y 向下）。
/// </summary>
public readonly record struct PointF(double X, double Y)
{
    public static PointF Zero => new(0, 0);

    public static PointF operator +(PointF a, PointF b) => new(a.X + b.X, a.Y + b.Y);
    public static PointF operator -(PointF a, PointF b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X:F1}, {Y:F1})";
}
