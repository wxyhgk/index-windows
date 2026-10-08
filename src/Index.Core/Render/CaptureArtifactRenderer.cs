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
        => RenderPng(basePng, layers, maxPixelDimension: null);

    public static byte[] RenderPreviewPng(
        byte[] basePng,
        Layers<ImageSpace> layers,
        int maxPixelDimension = 512)
    {
        if (maxPixelDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPixelDimension));
        return RenderPng(basePng, layers, maxPixelDimension);
    }

    private static byte[] RenderPng(
        byte[] basePng,
        Layers<ImageSpace> layers,
        int? maxPixelDimension)
    {
        ArgumentNullException.ThrowIfNull(basePng);
        ArgumentNullException.ThrowIfNull(layers);

        using var bitmap = SKBitmap.Decode(basePng)
            ?? throw new ArgumentException("无法解码截图 PNG。", nameof(basePng));
        using var pixelated = layers.Elements.Any(layer => layer.Kind == LayerKind.Pixelate)
            ? LayerRenderer.ApplyPixelate(bitmap, layers.Elements)
            : null;
        using var composedSurface = SKSurface.Create(new SKImageInfo(
            bitmap.Width,
            bitmap.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        var composedCanvas = composedSurface.Canvas;
        composedCanvas.Clear(SKColors.Transparent);
        composedCanvas.DrawBitmap(pixelated ?? bitmap, 0, 0);

        LayerRenderer.Render(
            layers.Elements,
            composedCanvas,
            lineWidth: 1,
            fontSize: 16,
            bitmap.Width,
            bitmap.Height);
        composedCanvas.Flush();

        var crop = ResolveCrop(layers.Elements, bitmap.Width, bitmap.Height);
        double scale = maxPixelDimension is { } maximum
            ? Math.Min(1d, maximum / (double)Math.Max(crop.Width, crop.Height))
            : 1d;
        int outputWidth = Math.Max(1, (int)Math.Round(crop.Width * scale));
        int outputHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));
        if (scale == 1
            && crop.Left == 0
            && crop.Top == 0
            && crop.Right == bitmap.Width
            && crop.Bottom == bitmap.Height)
        {
            using var fullImage = composedSurface.Snapshot();
            using var fullData = fullImage.Encode(SKEncodedImageFormat.Png, 100);
            return fullData.ToArray();
        }
        using var outputSurface = SKSurface.Create(new SKImageInfo(
            outputWidth,
            outputHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        var outputCanvas = outputSurface.Canvas;
        outputCanvas.Clear(SKColors.Transparent);
        using var composedImage = composedSurface.Snapshot();
        using var outputPaint = new SKPaint();
        outputCanvas.DrawImage(
            composedImage,
            crop,
            new SKRect(0, 0, outputWidth, outputHeight),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            outputPaint);
        outputCanvas.Flush();

        using var image = outputSurface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKRect ResolveCrop(
        IReadOnlyList<Layer> layers,
        int imageWidth,
        int imageHeight)
    {
        var layer = layers.LastOrDefault(item => item.Kind == LayerKind.Crop);
        if (layer is null)
            return new SKRect(0, 0, imageWidth, imageHeight);
        var rect = layer.Rect.Standardized();
        float left = Math.Clamp((float)Math.Floor(rect.MinX), 0, imageWidth);
        float top = Math.Clamp((float)Math.Floor(rect.MinY), 0, imageHeight);
        float right = Math.Clamp((float)Math.Ceiling(rect.MaxX), 0, imageWidth);
        float bottom = Math.Clamp((float)Math.Ceiling(rect.MaxY), 0, imageHeight);
        return right - left >= 1 && bottom - top >= 1
            ? new SKRect(left, top, right, bottom)
            : new SKRect(0, 0, imageWidth, imageHeight);
    }
}
