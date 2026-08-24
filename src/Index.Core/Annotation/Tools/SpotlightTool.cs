using SkiaSharp;

namespace Index.Annotation.Tools;

public sealed class SpotlightTool : AnnotationToolDescriptorBase
{
    public override AnnotationTool Tool => AnnotationTool.Spotlight;
    public override ToolStyleAxis[] Axes => new[] { ToolStyleAxis.Dim };
    public override bool IsPinnedToBar => true;
    public override bool DrawsMerged => true;

    public override Layer MakeLayer(ToolLayerContext context)
    {
        var layer = base.MakeLayer(context);
        layer.Dim = context.Style.ValueFor(ToolStyleAxis.Dim);
        return layer;
    }

    /// <summary>
    /// 聚光灯：所有亮区合成一张遮罩，区域外整体压暗。
    /// 在其余矢量层之下绘制。
    /// </summary>
    public override void DrawMerged(IReadOnlyList<Layer> layers, SKCanvas canvas, double lineWidth, double fontSize, int canvasWidth, int canvasHeight)
    {
        if (layers.Count == 0) return;

        float dim = (float)(layers[0].Dim ?? 0.55);

        using var dimPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)(dim * 255)),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        // 用 even-odd 规则：画整个画布，再挖掉亮区
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(new SKRect(0, 0, canvasWidth, canvasHeight));
        foreach (var layer in layers)
        {
            var rect = layer.Rect.Standardized();
            path.AddRect(new SKRect((float)rect.MinX, (float)rect.MinY, (float)rect.MaxX, (float)rect.MaxY));
        }

        canvas.DrawPath(path, dimPaint);
    }
}
