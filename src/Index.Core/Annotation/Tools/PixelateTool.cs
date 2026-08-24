using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class PixelateTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Pixelate;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.BlockSize };
    public override bool IsPinnedToBar => true;

    public override Layer MakeLayer(ToolLayerContext context)
    {
        var layer = base.MakeLayer(context);
        return layer with
        {
            BlockScale = context.Style.ValueFor(ToolStyleAxis.BlockSize)
        };
    }

    /// <summary>
    /// 马赛克是图像级滤镜，不参与逐层矢量绘制。
    /// 由渲染器在合成阶段用预计算的贴片绘制。
    /// </summary>
    public override void Draw(Layer layer, SKCanvas canvas, double lineWidth, double fontSize)
    {
        // 马赛克由渲染器特殊处理，这里不画
    }
}
