using Index.Render;
using SkiaSharp;

namespace Index.Tests;

public sealed class PinPixelRendererTests
{
    [Fact]
    public void DecodeAndHighlight_ProducesReadyBgraSurface()
    {
        using var source = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Green);
        using var image = SKImage.FromBitmap(source);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var decoded = PinPixelRenderer.DecodePng(encoded.ToArray());
        var highlighted = PinPixelRenderer.CreateHighlighted(decoded, 4, 2);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(8, highlighted.Width);
        Assert.Equal(6, highlighted.Height);
        Assert.Equal(highlighted.Width * highlighted.Height * 4, highlighted.Pixels.Length);
        Assert.Equal(new byte[] { 0xFF, 0xA3, 0x69, 0xFF }, highlighted.Pixels[..4]);
    }

    [Fact]
    public void PixelBuffer_RejectsMismatchedStorage()
    {
        Assert.Throws<ArgumentException>(() => new PinPixelBuffer(new byte[3], 1, 1));
    }
}
