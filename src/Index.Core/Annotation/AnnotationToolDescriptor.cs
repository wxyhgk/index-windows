using SkiaSharp;

namespace Index.Annotation;

/// <summary>
/// 一个标注工具的完整契约。
/// 加一个工具 = 新写一个实现 + 在 BuiltinTools.All 里加一行。
/// 对应 macOS 端 AnnotationToolDescriptor 协议。
/// </summary>
public interface IAnnotationToolDescriptor
{
    AnnotationTool Tool { get; }
    ToolStyleAxis[] Axes { get; }
    ToolStyle DefaultStyle { get; }
    ToolInput Input { get; }
    bool IsPinnedToBar { get; }

    /// <summary>造一个新层。</summary>
    Layer MakeLayer(ToolLayerContext context);

    /// <summary>命中测试（画布空间）。</summary>
    bool HitTest(Layer layer, PointF point, double tolerance);

    /// <summary>参与缩放的控制点。默认 8 个全上。</summary>
    ResizeHandle[] ResizeHandles { get; }

    /// <summary>控制点画在图层的什么位置。</summary>
    PointF HandleLocation(ResizeHandle handle, Layer layer);

    /// <summary>控制点画成端点圆点（线段类）还是方块（矩形类）。</summary>
    bool UsesEndpointHandles { get; }

    /// <summary>按控制点缩放。</summary>
    Layer Resize(Layer original, ResizeHandle handle, PointF delta, double pixelScale);

    /// <summary>缩到多小就算「缩没了」。</summary>
    bool MeetsMinimumSize(Layer layer);

    /// <summary>画一层（SkiaSharp 上下文）。</summary>
    void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize);

    /// <summary>这一类图层要在其余矢量层之下、且多层合并成一次绘制。</summary>
    bool DrawsMerged { get; }

    /// <summary>drawsMerged 为真时的合并绘制。</summary>
    void DrawMerged(IReadOnlyList<Layer> layers, SKCanvas canvas, double lineWidth, double fontSize, int canvasWidth, int canvasHeight);
}

/// <summary>
/// 默认实现基类。绝大多数工具只需声明 Tool/Axes/DefaultStyle。
/// </summary>
public abstract class AnnotationToolDescriptorBase : IAnnotationToolDescriptor
{
    public abstract AnnotationTool Tool { get; }
    public abstract ToolStyleAxis[] Axes { get; }
    public virtual ToolStyle DefaultStyle => new ToolStyle();
    public virtual ToolInput Input => ToolInput.Drag;
    public virtual bool IsPinnedToBar => false;
    public virtual ResizeHandle[] ResizeHandles => ResizeHandleExtensions.All;
    public virtual bool UsesEndpointHandles => false;

    public virtual PointF HandleLocation(ResizeHandle handle, Layer layer)
        => handle.PointIn(layer.HandleBounds);

    public virtual Layer MakeLayer(ToolLayerContext context)
        => BaseLayer(context);

    protected Layer BaseLayer(ToolLayerContext context)
    {
        return new Layer(
            Tool.ToLayerKind(),
            new LRect(context.From, context.To),
            context.Color,
            context.Style.ValueFor(ToolStyleAxis.Width) * context.StrokeScale,
            "",
            context.Style.ValueFor(ToolStyleAxis.FontSize) * context.StrokeScale)
        {
            BlockScale = Axes.Contains(ToolStyleAxis.BlockSize)
                ? context.Style.ValueFor(ToolStyleAxis.BlockSize) : null,
            Dim = Axes.Contains(ToolStyleAxis.Dim)
                ? context.Style.ValueFor(ToolStyleAxis.Dim) : null
        };
    }

    public virtual bool HitTest(Layer layer, PointF point, double tolerance)
        => layer.HandleBounds.InsetBy(-tolerance, -tolerance).Contains(point);

    public virtual Layer Resize(Layer original, ResizeHandle handle, PointF delta, double pixelScale)
    {
        var newRect = handle.Apply(delta, original.Rect);
        return original with { Rect = newRect };
    }

    public virtual bool MeetsMinimumSize(Layer layer)
    {
        var rect = layer.Rect.Standardized();
        return rect.W >= ToolGeometry.MinimumSide && rect.H >= ToolGeometry.MinimumSide;
    }

    public virtual void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize) { }
    public virtual bool DrawsMerged => false;
    public virtual void DrawMerged(IReadOnlyList<Layer> layers, SKCanvas canvas, double lineWidth, double fontSize, int canvasWidth, int canvasHeight) { }
}

/// <summary>
/// 工具实现之间共用的几何小工具。
/// </summary>
public static class ToolGeometry
{
    public const double MinimumSide = 4.0;

    /// <summary>点到线段的距离。</summary>
    public static double DistanceToSegment(PointF p, PointF a, PointF b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0)
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        double closestX = a.X + t * dx;
        double closestY = a.Y + t * dy;
        return Math.Sqrt((p.X - closestX) * (p.X - closestX) + (p.Y - closestY) * (p.Y - closestY));
    }
}

/// <summary>
/// 工具注册表。BuiltinTools.All 是唯一的登记处。
/// 对应 macOS 端 ToolRegistry。
/// </summary>
public static class ToolRegistry
{
    public static IReadOnlyList<IAnnotationToolDescriptor> Descriptors => BuiltinTools.All;

    public static IAnnotationToolDescriptor DescriptorFor(AnnotationTool tool)
        => Descriptors.FirstOrDefault(d => d.Tool == tool)
           ?? new FallbackTool(tool);

    public static IAnnotationToolDescriptor? DescriptorFor(LayerKind kind)
    {
        var tool = AnnotationToolExtensions.FromKind(kind);
        return tool.HasValue ? DescriptorFor(tool.Value) : null;
    }

    public static IReadOnlyList<AnnotationTool> PinnedTools
        => Descriptors.Where(d => d.IsPinnedToBar).Select(d => d.Tool).ToList();

    private sealed class FallbackTool : AnnotationToolDescriptorBase
    {
        public FallbackTool(AnnotationTool tool) { _tool = tool; }
        private readonly AnnotationTool _tool;
        public override AnnotationTool Tool => _tool;
        public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Color, ToolStyleAxis.Width };
    }
}
