using Index.Annotation;
using SkiaSharp;

namespace Index.Render;

/// <summary>
/// 把不可变选区底图与图像空间标注合成为最终 PNG。
/// 后续保存、复制和钉图动作共享这个入口，避免各自重复实现渲染。
/// </summary>
public static class CaptureArtifactRenderer
{
    public static byte[] RenderPng(byte[] basePng, Layers<ImageSpace> layers)
    {
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentNullException.ThrowIfNull(layers);

        using var bitmap = SKBitmap.Decode(basePng)
            ?? throw new ArgumentException("无法解码截图 PNG。", nameof(basePng));
        using var surface = SKSurface.Create(new SKImageInfo(
            bitmap.Width,
            bitmap.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(bitmap, 0, 0);

        LayerRenderer.Render(
            layers.Elements,
            canvas,
            lineWidth: 1,
            fontSize: 16,
            bitmap.Width,
            bitmap.Height);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
