using Index.Pin;
using Index.Ocr;
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
        int imageHeight,
        IReadOnlyList<OcrPixelRect>? selectedTextBounds = null,
        int selectionCoordinateWidth = 0,
        int selectionCoordinateHeight = 0)
    {
        var image = Resize(source, imageWidth, imageHeight);
        byte[] imagePixels = selectedTextBounds is { Count: > 0 }
            ? ComposeTextSelection(
                image,
                selectedTextBounds,
                selectionCoordinateWidth,
                selectionCoordinateHeight)
            : image.Pixels;
        return new PinPixelBuffer(
            PinHighlight.ComposePremultipliedBgra(
                imagePixels,
                image.Width,
                image.Height),
            checked(image.Width + PinHighlight.Thickness * 2),
            checked(image.Height + PinHighlight.Thickness * 2));
    }

    private static byte[] ComposeTextSelection(
        PinPixelBuffer image,
        IReadOnlyList<OcrPixelRect> selectedTextBounds,
        int coordinateWidth,
        int coordinateHeight)
    {
        if (coordinateWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(coordinateWidth));
        if (coordinateHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(coordinateHeight));

        var pixels = image.Pixels.ToArray();
        double scaleX = image.Width / (double)coordinateWidth;
        double scaleY = image.Height / (double)coordinateHeight;
        foreach (var bounds in selectedTextBounds)
        {
            var normalized = bounds.Normalized();
            int left = Math.Clamp((int)Math.Floor(normalized.X * scaleX), 0, image.Width);
            int top = Math.Clamp((int)Math.Floor(normalized.Y * scaleY), 0, image.Height);
            int right = Math.Clamp((int)Math.Ceiling(normalized.Right * scaleX), 0, image.Width);
            int bottom = Math.Clamp((int)Math.Ceiling(normalized.Bottom * scaleY), 0, image.Height);
            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    bool stroke = x == left || x == right - 1 || y == top || y == bottom - 1;
                    BlendBlue(pixels, (y * image.Width + x) * 4, stroke ? (byte)210 : (byte)72);
                }
            }
        }
        return pixels;
    }

    private static void BlendBlue(byte[] pixels, int offset, byte alpha)
    {
        const byte blue = 0xFF;
        const byte green = 0x7D;
        const byte red = 0x2F;
        int inverse = byte.MaxValue - alpha;
        pixels[offset] = (byte)((blue * alpha + pixels[offset] * inverse + 127) / 255);
        pixels[offset + 1] = (byte)((green * alpha + pixels[offset + 1] * inverse + 127) / 255);
        pixels[offset + 2] = (byte)((red * alpha + pixels[offset + 2] * inverse + 127) / 255);
        pixels[offset + 3] = (byte)(alpha + (pixels[offset + 3] * inverse + 127) / 255);
    }
}
