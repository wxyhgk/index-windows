namespace Ketcher.WinUI3.Core.Geometry;

/// <summary>
/// 文档坐标到屏幕坐标的变换。统一用于绘制和命中测试。
/// 文档坐标原点 (0,0) 对应屏幕坐标 (OffsetX, OffsetY)，缩放因子 Scale。
/// </summary>
public sealed class ViewportTransform
{
    public double Scale { get; private set; } = 1.0;
    public double OffsetX { get; private set; } = 0.0;
    public double OffsetY { get; private set; } = 0.0;

    private const double MinScale = 0.05;
    private const double MaxScale = 50.0;

    /// <summary>文档坐标 → 屏幕坐标。</summary>
    public Vector2 ToScreen(Vector2 doc) =>
        new(doc.X * Scale + OffsetX, doc.Y * Scale + OffsetY);

    /// <summary>屏幕坐标 → 文档坐标。</summary>
    public Vector2 ToDocument(Vector2 screen) =>
        new((screen.X - OffsetX) / Scale, (screen.Y - OffsetY) / Scale);

    /// <summary>平移（屏幕像素）。</summary>
    public void Pan(double dxPixels, double dyPixels)
    {
        OffsetX += dxPixels;
        OffsetY += dyPixels;
    }

    /// <summary>以屏幕点为中心缩放。</summary>
    public void Zoom(double factor, double centerXPixels, double centerYPixels)
    {
        double newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        double actualFactor = newScale / Scale;

        // 保持中心点不变
        OffsetX = centerXPixels - (centerXPixels - OffsetX) * actualFactor;
        OffsetY = centerYPixels - (centerYPixels - OffsetY) * actualFactor;
        Scale = newScale;
    }

    /// <summary>缩放到适合窗口，带边距。</summary>
    public void FitToContent(Vector2 contentMin, Vector2 contentMax, double viewportWidth, double viewportHeight, double margin = 40)
    {
        double contentWidth = contentMax.X - contentMin.X;
        double contentHeight = contentMax.Y - contentMin.Y;

        if (contentWidth <= 0 && contentHeight <= 0)
        {
            // 单点或空内容
            Scale = 1.0;
            OffsetX = viewportWidth / 2 - contentMin.X * Scale;
            OffsetY = viewportHeight / 2 - contentMin.Y * Scale;
            return;
        }

        double availableWidth = Math.Max(viewportWidth - 2 * margin, 1);
        double availableHeight = Math.Max(viewportHeight - 2 * margin, 1);

        double scaleX = availableWidth / contentWidth;
        double scaleY = availableHeight / contentHeight;
        Scale = Math.Clamp(Math.Min(scaleX, scaleY), MinScale, MaxScale);

        // 居中
        OffsetX = (viewportWidth - contentWidth * Scale) / 2 - contentMin.X * Scale;
        OffsetY = (viewportHeight - contentHeight * Scale) / 2 - contentMin.Y * Scale;
    }

    /// <summary>获取内容边界框（文档坐标）。</summary>
    public static (Vector2 min, Vector2 max) GetContentBounds(
        IReadOnlyList<(double x, double y)> points)
    {
        if (points.Count == 0)
            return (new Vector2(0, 0), new Vector2(0, 0));

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var (x, y) in points)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        return (new Vector2(minX, minY), new Vector2(maxX, maxY));
    }

    /// <summary>重置为默认变换。</summary>
    public void Reset()
    {
        Scale = 1.0;
        OffsetX = 0;
        OffsetY = 0;
    }
}
