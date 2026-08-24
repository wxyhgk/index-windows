namespace Index.Annotation;

/// <summary>
/// 标注工具枚举。对应 macOS 端 AnnotationTool。
/// </summary>
public enum AnnotationTool
{
    Rect,
    Ellipse,
    Arrow,
    Text,
    Highlight,
    Pixelate,
    Line,
    Crop,
    Counter,
    Spotlight,
    Dimension
}

public static class AnnotationToolExtensions
{
    public static LayerKind ToLayerKind(this AnnotationTool tool)
    {
        return tool switch
        {
            AnnotationTool.Rect => LayerKind.Rect,
            AnnotationTool.Ellipse => LayerKind.Ellipse,
            AnnotationTool.Arrow => LayerKind.Arrow,
            AnnotationTool.Text => LayerKind.Text,
            AnnotationTool.Highlight => LayerKind.Highlight,
            AnnotationTool.Pixelate => LayerKind.Pixelate,
            AnnotationTool.Line => LayerKind.Line,
            AnnotationTool.Crop => LayerKind.Crop,
            AnnotationTool.Counter => LayerKind.Counter,
            AnnotationTool.Spotlight => LayerKind.Spotlight,
            AnnotationTool.Dimension => LayerKind.Dimension,
            _ => LayerKind.Rect
        };
    }

    public static string Title(this AnnotationTool tool)
    {
        return tool switch
        {
            AnnotationTool.Rect => "矩形",
            AnnotationTool.Ellipse => "椭圆",
            AnnotationTool.Arrow => "箭头",
            AnnotationTool.Text => "文字",
            AnnotationTool.Highlight => "高亮",
            AnnotationTool.Pixelate => "马赛克",
            AnnotationTool.Line => "直线",
            AnnotationTool.Crop => "裁剪",
            AnnotationTool.Counter => "序号",
            AnnotationTool.Spotlight => "聚光灯",
            AnnotationTool.Dimension => "测量",
            _ => tool.ToString()
        };
    }

    /// <summary>持久化用的稳定标识。</summary>
    public static string Id(this AnnotationTool tool) => tool.ToLayerKind().ToString().ToLowerInvariant();

    /// <summary>从图层种类反查工具。</summary>
    public static AnnotationTool? FromKind(LayerKind kind)
    {
        return kind switch
        {
            LayerKind.Rect => AnnotationTool.Rect,
            LayerKind.Ellipse => AnnotationTool.Ellipse,
            LayerKind.Arrow => AnnotationTool.Arrow,
            LayerKind.Text => AnnotationTool.Text,
            LayerKind.Highlight => AnnotationTool.Highlight,
            LayerKind.Pixelate => AnnotationTool.Pixelate,
            LayerKind.Line => AnnotationTool.Line,
            LayerKind.Crop => AnnotationTool.Crop,
            LayerKind.Counter => AnnotationTool.Counter,
            LayerKind.Spotlight => AnnotationTool.Spotlight,
            LayerKind.Dimension => AnnotationTool.Dimension,
            _ => null
        };
    }
}

/// <summary>
/// 工具落笔方式。
/// </summary>
public enum ToolInput
{
    /// <summary>拖出形体：矩形、椭圆、箭头、线、马赛克…</summary>
    Drag,
    /// <summary>点一下就落一层：文字、序号。</summary>
    Click
}

/// <summary>
/// 造一个新图层时能拿到的全部材料。
/// 对应 macOS 端 ToolLayerContext。
/// </summary>
public sealed class ToolLayerContext
{
    public required PointF From { get; init; }
    public required PointF To { get; init; }
    public required ToolStyle Style { get; init; }
    public required LColor Color { get; init; }
    public required double StrokeScale { get; init; }
    public required double PixelScale { get; init; }
    public required IReadOnlyList<Layer> Existing { get; init; }
}
