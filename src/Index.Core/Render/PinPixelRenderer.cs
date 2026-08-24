using Index.Pin;
using SkiaSharp;

namespace Index.Render;

/// <summary>
/// Immutable description of a premultiplied BGRA pixel surface.
/// The byte array is owned by this instance and must not be mutated by callers.
/// </summary>
public sealed class PinPixelBuffer
{
    public PinPixelBuffer(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        int expectedLength = checked(width * height * 4);
        if (pixels.Length != expectedLength)
            throw new ArgumentException(
                $"Expected {expectedLength} BGRA bytes, received {pixels.Length}.",
                nameof(pixels));

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
}

/// <summary>
/// Owns all Skia-based pixel preparation needed by a pin surface. Window classes only
/// consume ready-to-present BGRA buffers and never decode or resample images themselves.
/// </summary>
public static class PinPixelRenderer
{
    public static PinPixelBuffer DecodePng(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length == 0)
            throw new InvalidDataException("The pin PNG is empty.");

        using var bitmap = SKBitmap.Decode(png)
            ?? throw new InvalidDataException("Unable to decode pin PNG.");
        using var converted = new SKBitmap(new SKImageInfo(
            bitmap.Width,
            bitmap.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));
        using (var canvas = new SKCanvas(converted))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, 0, 0);
            canvas.Flush();
        }

        return new PinPixelBuffer(
            converted.GetPixelSpan().ToArray(),
            bitmap.Width,
            bitmap.Height);
    }

    public static PinPixelBuffer Resize(PinPixelBuffer source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (width == source.Width && height == source.Height)
            return source;

        var sourceInfo = new SKImageInfo(
            source.Width,
            source.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var bitmap = new SKBitmap(sourceInfo);
        source.Pixels.AsSpan().CopyTo(bitmap.GetPixelSpan());
        using var resized = bitmap.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("Unable to resize pin pixels.");
        return new PinPixelBuffer(resized.GetPixelSpan().ToArray(), width, height);
    }

    public static PinPixelBuffer CreateHighlighted(
        PinPixelBuffer source,
        int imageWidth,
        int imageHeight)
    {
        var image = Resize(source, imageWidth, imageHeight);
        return new PinPixelBuffer(
            PinHighlight.ComposePremultipliedBgra(
                image.Pixels,
                image.Width,
                image.Height),
            checked(image.Width + PinHighlight.Thickness * 2),
            checked(image.Height + PinHighlight.Thickness * 2));
    }
}
