namespace Index.Annotation;

/// <summary>
/// 矩形的 8 个调整控制点，外加「内部」用于整体拖动。
/// 对应 macOS 端 ResizeHandle。
/// </summary>
public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Inside
}

public static class ResizeHandleExtensions
{
    /// <summary>参与缩放和命中的 8 个控制点。</summary>
    public static readonly ResizeHandle[] All =
    {
        ResizeHandle.TopLeft, ResizeHandle.Top, ResizeHandle.TopRight, ResizeHandle.Right,
        ResizeHandle.BottomRight, ResizeHandle.Bottom, ResizeHandle.BottomLeft, ResizeHandle.Left
    };

    /// <summary>控制点在矩形上的位置（Y 向下）。</summary>
    public static PointF PointIn(this ResizeHandle handle, LRect rect)
    {
        return handle switch
        {
            ResizeHandle.TopLeft => new PointF(rect.MinX, rect.MinY),
            ResizeHandle.Top => new PointF(rect.MidX, rect.MinY),
            ResizeHandle.TopRight => new PointF(rect.MaxX, rect.MinY),
            ResizeHandle.Right => new PointF(rect.MaxX, rect.MidY),
            ResizeHandle.BottomRight => new PointF(rect.MaxX, rect.MaxY),
            ResizeHandle.Bottom => new PointF(rect.MidX, rect.MaxY),
            ResizeHandle.BottomLeft => new PointF(rect.MinX, rect.MaxY),
            ResizeHandle.Left => new PointF(rect.MinX, rect.MidY),
            ResizeHandle.Inside => new PointF(rect.MidX, rect.MidY),
            _ => new PointF(rect.MidX, rect.MidY)
        };
    }

    /// <summary>
    /// 把位移应用到矩形上。拖过对边时自动翻转，不会出现负尺寸。
    /// </summary>
    public static LRect Apply(this ResizeHandle handle, PointF delta, LRect rect)
    {
        double minX = rect.MinX, maxX = rect.MaxX;
        double minY = rect.MinY, maxY = rect.MaxY;

        switch (handle)
        {
            case ResizeHandle.TopLeft: minX += delta.X; minY += delta.Y; break;
            case ResizeHandle.Top: minY += delta.Y; break;
            case ResizeHandle.TopRight: maxX += delta.X; minY += delta.Y; break;
            case ResizeHandle.Right: maxX += delta.X; break;
            case ResizeHandle.BottomRight: maxX += delta.X; maxY += delta.Y; break;
            case ResizeHandle.Bottom: maxY += delta.Y; break;
            case ResizeHandle.BottomLeft: minX += delta.X; maxY += delta.Y; break;
            case ResizeHandle.Left: minX += delta.X; break;
            case ResizeHandle.Inside: return rect.OffsetBy(delta.X, delta.Y);
        }

        return new LRect(
            Math.Min(minX, maxX),
            Math.Min(minY, maxY),
            Math.Abs(maxX - minX),
            Math.Abs(maxY - minY));
    }

    public static bool IsMove(this ResizeHandle handle) => handle == ResizeHandle.Inside;
}
