namespace Index.Annotation;

/// <summary>
/// 可编码颜色（0-1 浮点 RGBA）。
/// 对应 macOS 端 LColor。
/// </summary>
public sealed record LColor
{
    public double R { get; init; }
    public double G { get; init; }
    public double B { get; init; }
    public double A { get; init; }

    public LColor(double r, double g, double b, double a = 1.0)
    {
        R = r; G = g; B = b; A = a;
    }

    public static readonly LColor Red = new(0.98, 0.22, 0.22, 1);
    public static readonly LColor Yellow = new(1.0, 0.85, 0.15, 0.45);

    public LColor WithAlpha(double alpha) => new(R, G, B, alpha);

    /// <summary>转为 SkiaSharp SKColor。</summary>
    public SkiaSharp.SKColor ToSKColor()
    {
        return new SkiaSharp.SKColor(
            (byte)(R * 255),
            (byte)(G * 255),
            (byte)(B * 255),
            (byte)(A * 255));
    }

    public override string ToString() => $"LColor({R:F2}, {G:F2}, {B:F2}, {A:F2})";
}
