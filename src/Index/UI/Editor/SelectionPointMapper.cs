using Windows.Foundation;
using Index.Annotation;
using Index.Capture;

namespace Index.UI.Editor;

/// <summary>
/// 选区坐标映射：UI Point ↔ SelectionPoint、ResizeHandle ↔ SelectionResizeHandle。
/// 从 OverlayWindow 拆出，集中管理坐标转换逻辑。
/// </summary>
public static class SelectionPointMapper
{
    public static SelectionPoint ToSelectionPoint(Point point) => new(point.X, point.Y);

    public static SelectionResizeHandle ToSelectionHandle(ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => SelectionResizeHandle.TopLeft,
        ResizeHandle.Top => SelectionResizeHandle.Top,
        ResizeHandle.TopRight => SelectionResizeHandle.TopRight,
        ResizeHandle.Right => SelectionResizeHandle.Right,
        ResizeHandle.BottomRight => SelectionResizeHandle.BottomRight,
        ResizeHandle.Bottom => SelectionResizeHandle.Bottom,
        ResizeHandle.BottomLeft => SelectionResizeHandle.BottomLeft,
        ResizeHandle.Left => SelectionResizeHandle.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, null)
    };
}
