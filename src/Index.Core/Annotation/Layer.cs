namespace Index.Annotation;

/// <summary>
/// 一个标注图层。永远不写回原图，只作为修订链的一部分被序列化。
/// 对应 macOS 端 Layer。
/// </summary>
public sealed record Layer
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public LayerKind Kind { get; init; }

    public LRect Rect { get; set; }

    public LColor Color { get; set; }

    public double LineWidth { get; set; }

    public string Text { get; set; } = "";

    public double FontSize { get; set; }

    /// <summary>马赛克块大小倍率。</summary>
    public double? BlockScale { get; set; }

    /// <summary>聚光灯压暗程度（0-1）。</summary>
    public double? Dim { get; set; }

    public Layer(LayerKind kind, LRect rect, LColor color, double lineWidth, string text = "", double fontSize = 0)
    {
        Kind = kind;
        Rect = rect;
        Color = color;
        LineWidth = lineWidth;
        Text = text;
        FontSize = fontSize;
    }

    /// <summary>
    /// 控制点与选中框依据的形体范围。
    /// text 的 Rect 只存锚点，实际范围按字号估算。
    /// </summary>
    public LRect HandleBounds
    {
        get
        {
            if (Kind == LayerKind.Text)
            {
                double estimatedWidth = Text.Length * FontSize * 0.6;
                double estimatedHeight = FontSize * 1.2;
                return new LRect(Rect.X, Rect.Y, estimatedWidth, estimatedHeight);
            }
            return Rect.Standardized();
        }
    }

    public bool IsEffect => Kind is LayerKind.Watermark or LayerKind.CaptureInfo or LayerKind.Frame or LayerKind.Backdrop;
}

/// <summary>
/// 图层种类。对应 macOS 端 Layer.Kind。
/// </summary>
public enum LayerKind
{
    Rect,
    Ellipse,
    Arrow,
    Line,
    Text,
    Highlight,
    Pixelate,
    Crop,
    Counter,
    Spotlight,
    Dimension,
    Backdrop,
    Watermark,
    Frame,
    CaptureInfo
}
