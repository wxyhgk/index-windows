namespace Index.Annotation;

/// <summary>
/// 工具可调的样式轴。工具条据此决定显示哪些控件。
/// 对应 macOS 端 ToolStyleAxis。
/// </summary>
public enum ToolStyleAxis
{
    Color,
    Width,
    FontSize,
    Opacity,
    BlockSize,
    Dim
}

/// <summary>
/// 单根样式轴的完整声明 —— 唯一改动点。
/// 对应 macOS 端 ToolStyleAxisDescriptor。
/// </summary>
public sealed class ToolStyleAxisDescriptor
{
    public ToolStyleAxis Axis { get; }
    public int Order { get; }
    public string Title { get; }
    public double[] Steps { get; }
    public int DefaultIndex { get; }
    public string CodingKey { get; }

    public ToolStyleAxisDescriptor(ToolStyleAxis axis, int order, string title, double[] steps, int defaultIndex, string codingKey)
    {
        Axis = axis; Order = order; Title = title; Steps = steps; DefaultIndex = defaultIndex; CodingKey = codingKey;
    }

    // 注册表（唯一真相）
    public static readonly ToolStyleAxisDescriptor[] All =
    {
        new(ToolStyleAxis.Color, 0, "颜色", Array.Empty<double>(), 0, "colorIndex"),
        new(ToolStyleAxis.Width, 100, "粗细", new double[] { 2, 4, 8 }, 1, "widthIndex"),
        new(ToolStyleAxis.FontSize, 200, "字号", new double[] { 16, 24, 40 }, 1, "fontSizeIndex"),
        new(ToolStyleAxis.Opacity, 300, "透明度", new double[] { 0.25, 0.40, 0.60 }, 1, "opacityIndex"),
        new(ToolStyleAxis.BlockSize, 400, "颗粒", new double[] { 0.6, 1.0, 1.8 }, 1, "blockSizeIndex"),
        new(ToolStyleAxis.Dim, 500, "压暗", new double[] { 0.40, 0.55, 0.75 }, 1, "dimIndex"),
    };

    public static ToolStyleAxisDescriptor DescriptorFor(ToolStyleAxis axis)
    {
        return All.FirstOrDefault(d => d.Axis == axis)
            ?? throw new InvalidOperationException($"Missing descriptor for axis {axis}");
    }
}

/// <summary>
/// 一个工具记住的样式。每根轴存档位下标而不是数值。
/// 对应 macOS 端 ToolStyle。
/// </summary>
public sealed class ToolStyle : IEquatable<ToolStyle>
{
    public int ColorIndex { get; set; } = 0;
    public int WidthIndex { get; set; } = 1;
    public int FontSizeIndex { get; set; } = 1;
    public int OpacityIndex { get; set; } = 1;
    public int BlockSizeIndex { get; set; } = 1;
    public int DimIndex { get; set; } = 1;

    public ToolStyle() { }

    public ToolStyle(int colorIndex, int widthIndex, int fontSizeIndex)
    {
        ColorIndex = colorIndex;
        WidthIndex = widthIndex;
        FontSizeIndex = fontSizeIndex;
    }

    public int IndexFor(ToolStyleAxis axis)
    {
        return axis switch
        {
            ToolStyleAxis.Color => ColorIndex,
            ToolStyleAxis.Width => WidthIndex,
            ToolStyleAxis.FontSize => FontSizeIndex,
            ToolStyleAxis.Opacity => OpacityIndex,
            ToolStyleAxis.BlockSize => BlockSizeIndex,
            ToolStyleAxis.Dim => DimIndex,
            _ => 0
        };
    }

    public void SetIndex(int value, ToolStyleAxis axis)
    {
        switch (axis)
        {
            case ToolStyleAxis.Color: ColorIndex = value; break;
            case ToolStyleAxis.Width: WidthIndex = value; break;
            case ToolStyleAxis.FontSize: FontSizeIndex = value; break;
            case ToolStyleAxis.Opacity: OpacityIndex = value; break;
            case ToolStyleAxis.BlockSize: BlockSizeIndex = value; break;
            case ToolStyleAxis.Dim: DimIndex = value; break;
        }
    }

    /// <summary>取某根轴的数值。越界回中档。</summary>
    public double ValueFor(ToolStyleAxis axis)
    {
        var steps = ToolStyleAxisDescriptor.DescriptorFor(axis).Steps;
        if (steps.Length == 0) return 0;
        int i = IndexFor(axis);
        return i >= 0 && i < steps.Length ? steps[i] : steps[steps.Length / 2];
    }

    public ToolStyle Clone() => new()
    {
        ColorIndex = ColorIndex,
        WidthIndex = WidthIndex,
        FontSizeIndex = FontSizeIndex,
        OpacityIndex = OpacityIndex,
        BlockSizeIndex = BlockSizeIndex,
        DimIndex = DimIndex
    };

    public bool Equals(ToolStyle? other)
    {
        if (other is null) return false;
        return ColorIndex == other.ColorIndex
            && WidthIndex == other.WidthIndex
            && FontSizeIndex == other.FontSizeIndex
            && OpacityIndex == other.OpacityIndex
            && BlockSizeIndex == other.BlockSizeIndex
            && DimIndex == other.DimIndex;
    }

    public override bool Equals(object? obj) => Equals(obj as ToolStyle);
    public override int GetHashCode() => HashCode.Combine(ColorIndex, WidthIndex, FontSizeIndex, OpacityIndex, BlockSizeIndex, DimIndex);
}
