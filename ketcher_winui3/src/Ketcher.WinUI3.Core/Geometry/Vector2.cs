namespace Ketcher.WinUI3.Core.Geometry;

/// <summary>与设备无关的二维文档坐标。</summary>
public readonly record struct Vector2(double X, double Y)
{
    public static Vector2 Zero { get; } = new(0, 0);

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 v, double s) => new(v.X * s, v.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vector2 Normalized
    {
        get
        {
            double len = Length;
            return len > 0 ? new Vector2(X / len, Y / len) : Zero;
        }
    }

    public double DistanceTo(Vector2 other)
    {
        double dx = X - other.X, dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"({X:R}, {Y:R})";
}
