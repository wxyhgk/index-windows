using Index.Annotation.Tools;

namespace Index.Annotation;

/// <summary>
/// 内置标注工具的登记处。
/// 加一个工具 = 在 Tools/ 下新建文件 + 这里加一行。
/// 声明顺序 = 工具条顺序。
/// 对应 macOS 端 BuiltinTools。
/// </summary>
public static class BuiltinTools
{
    public static readonly IAnnotationToolDescriptor[] All =
    {
        new RectTool(),
        new EllipseTool(),
        new ArrowTool(),
        new LineTool(),
        new TextTool(),
        new HighlightTool(),
        new PixelateTool(),
        new CropTool(),
        new CounterTool(),
        new SpotlightTool(),
        new DimensionTool(),
    };
}
