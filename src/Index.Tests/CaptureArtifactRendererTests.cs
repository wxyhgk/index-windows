using Index.Annotation;
using Index.Render;
using SkiaSharp;

namespace Index.Tests;

public sealed class CaptureArtifactRendererTests
{
    [Fact]
    public void CropProducesClippedOutputWithoutDrawingTheEditorGuide()
    {
        byte[] source = SolidPng(10, 8, SKColors.White);
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Crop,
            new LRect(2, 1, 5, 4),
            LColor.Red,
            1));

        byte[] rendered = CaptureArtifactRenderer.RenderPng(source, layers);

        using var bitmap = SKBitmap.Decode(rendered);
        Assert.NotNull(bitmap);
        Assert.Equal(5, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
        Assert.All(
            Enumerable.Range(0, bitmap.Width * bitmap.Height),
            index => Assert.Equal(SKColors.White, bitmap.GetPixel(
                index % bitmap.Width,
                index / bitmap.Width)));
    }

    [Fact]
    public void PixelateChangesOnlyTheSelectedRegion()
    {
        byte[] source = GradientPng(12, 12);
        using var original = SKBitmap.Decode(source);
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Pixelate,
            new LRect(2, 2, 8, 8),
            LColor.Red,
            0)
        {
            BlockScale = 1
        });

        byte[] rendered = CaptureArtifactRenderer.RenderPng(source, layers);

        using var bitmap = SKBitmap.Decode(rendered);
        Assert.NotNull(bitmap);
        Assert.Equal(original.GetPixel(0, 0), bitmap.GetPixel(0, 0));
        Assert.Equal(original.GetPixel(11, 11), bitmap.GetPixel(11, 11));
        Assert.NotEqual(original.GetPixel(3, 3), bitmap.GetPixel(3, 3));
        Assert.Equal(bitmap.GetPixel(3, 3), bitmap.GetPixel(8, 8));
    }

    [Fact]
    public void PreviewLimitIsAppliedAfterCrop()
    {
        byte[] source = SolidPng(1000, 200, SKColors.White);
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Crop,
            new LRect(100, 20, 100, 100),
            LColor.Red,
            1));

        byte[] rendered = CaptureArtifactRenderer.RenderPreviewPng(
            source,
            layers,
            maxPixelDimension: 64);

        using var bitmap = SKBitmap.Decode(rendered);
        Assert.NotNull(bitmap);
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
    }

    private static byte[] SolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return Encode(bitmap);
    }

    private static byte[] GradientPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(
                    (byte)(x * 17),
                    (byte)(y * 17),
                    (byte)((x + y) * 8),
                    byte.MaxValue));
            }
        }
        return Encode(bitmap);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
