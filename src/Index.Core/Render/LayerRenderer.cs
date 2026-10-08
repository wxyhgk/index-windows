using Index.Annotation;
using SkiaSharp;

namespace Index.Render;

/// <summary>
/// 图层渲染器：把 Layers 画到 SKCanvas 上。
/// 对应 macOS 端 LayerRenderer。
/// 同一份代码服务预览（画布空间）和导出（图像像素空间）。
/// </summary>
public static class LayerRenderer
{
    /// <summary>
    /// 渲染所有图层到画布。
    /// </summary>
    /// <param name="layers">要渲染的图层列表。</param>
    /// <param name="canvas">目标画布。</param>
    /// <param name="lineWidth">线宽（已按空间换算）。</param>
    /// <param name="fontSize">字号（已按空间换算）。</param>
    public static void Render(IReadOnlyList<Layer> layers, SKCanvas canvas, double lineWidth, double fontSize, int canvasWidth, int canvasHeight)
    {
        // 第一步：合并绘制的层（聚光灯）—— 在其余矢量层之下
        var mergedLayers = layers
            .Where(l => !l.IsEffect && ToolRegistry.DescriptorFor(l.Kind)?.DrawsMerged == true)
            .ToList();
        if (mergedLayers.Count > 0)
        {
            var descriptor = ToolRegistry.DescriptorFor(mergedLayers[0].Kind)!;
            descriptor.DrawMerged(mergedLayers, canvas, lineWidth, fontSize, canvasWidth, canvasHeight);
        }

        // 第二步：逐层矢量绘制
        foreach (var layer in layers)
        {
            if (layer.IsEffect) continue;
            if (layer.Kind is LayerKind.Pixelate or LayerKind.Crop) continue;
            var descriptor = ToolRegistry.DescriptorFor(layer.Kind);
            if (descriptor is null) continue;
            if (descriptor.DrawsMerged) continue; // 已合并绘制
            descriptor.Draw(
                layer,
                canvas,
                layer.LineWidth > 0 ? layer.LineWidth : lineWidth,
                layer.FontSize > 0 ? layer.FontSize : fontSize);
        }
    }

    /// <summary>
    /// Applies every pixelate layer to a copy of the source bitmap. Layers use the
    /// same top-left image-pixel coordinates as vector rendering; pixels outside the
    /// normalized, clipped layer rectangles remain untouched.
    /// </summary>
    public static SKBitmap ApplyPixelate(
        SKBitmap source,
        IReadOnlyList<Layer> layers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(layers);
        var result = new SKBitmap(new SKImageInfo(
            source.Width,
            source.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        using (var copyCanvas = new SKCanvas(result))
        {
            copyCanvas.Clear(SKColors.Transparent);
            copyCanvas.DrawBitmap(source, 0, 0);
            copyCanvas.Flush();
        }

        foreach (var layer in layers.Where(item => item.Kind == LayerKind.Pixelate))
        {
            var rect = layer.Rect.Standardized();
            int left = Math.Clamp((int)Math.Floor(rect.MinX), 0, source.Width);
            int top = Math.Clamp((int)Math.Floor(rect.MinY), 0, source.Height);
            int right = Math.Clamp((int)Math.Ceiling(rect.MaxX), 0, source.Width);
            int bottom = Math.Clamp((int)Math.Ceiling(rect.MaxY), 0, source.Height);
            int width = right - left;
            int height = bottom - top;
            if (width < 2 || height < 2)
                continue;

            double requestedScale = Math.Max(0.25, layer.BlockScale ?? 1);
            int blockSize = Math.Max(
                4,
                (int)Math.Round(Math.Max(6, Math.Min(width, height) / 12d * requestedScale)));
            int smallWidth = Math.Max(1, width / blockSize);
            int smallHeight = Math.Max(1, height / blockSize);
            using var small = new SKBitmap(
                smallWidth,
                smallHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);
            using (var downCanvas = new SKCanvas(small))
            using (var currentImage = SKImage.FromBitmap(result))
            using (var paint = new SKPaint())
            {
                downCanvas.DrawImage(
                    currentImage,
                    new SKRect(left, top, right, bottom),
                    new SKRect(0, 0, smallWidth, smallHeight),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                    paint);
                downCanvas.Flush();
            }

            using var resultCanvas = new SKCanvas(result);
            using var smallImage = SKImage.FromBitmap(small);
            using var upPaint = new SKPaint();
            resultCanvas.DrawImage(
                smallImage,
                new SKRect(left, top, right, bottom),
                new SKSamplingOptions(SKFilterMode.Nearest),
                upPaint);
            resultCanvas.Flush();
        }

        return result;
    }

    /// <summary>
    /// 渲染选中图层的控制点。
    /// </summary>
    public static void RenderSelectionHandles(Layer layer, SKCanvas canvas)
    {
        var descriptor = ToolRegistry.DescriptorFor(layer.Kind);
        if (descriptor is null) return;

        var handles = descriptor.ResizeHandles;
        var handleBounds = layer.HandleBounds;

        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        foreach (var handle in handles)
        {
            var pos = descriptor.HandleLocation(handle, layer);
            if (descriptor.UsesEndpointHandles)
            {
                // 端点圆点
                paint.Color = SKColors.White;
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle((float)pos.X, (float)pos.Y, 5f, paint);
                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                canvas.DrawCircle((float)pos.X, (float)pos.Y, 5f, paint);
            }
            else
            {
                // 方块
                float size = 4;
                paint.Color = SKColors.White;
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawRect(new SKRect(
                    (float)pos.X - size,
                    (float)pos.Y - size,
                    (float)pos.X + size,
                    (float)pos.Y + size), paint);
                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1f;
                canvas.DrawRect(new SKRect(
                    (float)pos.X - size,
                    (float)pos.Y - size,
                    (float)pos.X + size,
                    (float)pos.Y + size), paint);
            }
        }

        // 选中框
        using var strokePaint = new SKPaint
        {
            Color = new SKColor(0, 122, 255),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        var bounds = handleBounds.Standardized();
        canvas.DrawRect(new SKRect(
            (float)bounds.MinX - 2,
            (float)bounds.MinY - 2,
            (float)bounds.MaxX + 2,
            (float)bounds.MaxY + 2), strokePaint);
    }

    /// <summary>
    /// 马赛克：把图像区域缩小再放大，产生像素化效果。
    /// </summary>
    public static byte[] Pixelate(byte[] pngData, int blockScale)
    {
        using var source = SKBitmap.Decode(pngData);
        if (source == null) return pngData;

        int blockSize = Math.Max(4, (int)(8 * blockScale));
        int smallW = Math.Max(1, source.Width / blockSize);
        int smallH = Math.Max(1, source.Height / blockSize);

        using var small = new SKBitmap(smallW, smallH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(small);
        using var sourceImage = SKImage.FromBitmap(source);
        using var paint = new SKPaint();
        canvas.DrawImage(
            sourceImage,
            new SKRect(0, 0, smallW, smallH),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint);

        // 放大回原始尺寸（最近邻插值）
        using var result = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var resultCanvas = new SKCanvas(result);
        using var smallImage = SKImage.FromBitmap(small);
        using var upPaint = new SKPaint();
        resultCanvas.DrawImage(
            smallImage,
            new SKRect(0, 0, source.Width, source.Height),
            new SKSamplingOptions(SKFilterMode.Nearest),
            upPaint);

        using var image = SKImage.FromBitmap(result);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
