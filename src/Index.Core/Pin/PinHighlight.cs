namespace Index.Pin;

/// <summary>Pure geometry and pixel composition for the non-destructive pin highlight.</summary>
public static class PinHighlight
{
    public const int Thickness = 2;

    public static PinRect OuterFrame(PinRect imageFrame) => new(
        imageFrame.X - Thickness,
        imageFrame.Y - Thickness,
        imageFrame.Width + Thickness * 2,
        imageFrame.Height + Thickness * 2);

    public static PinRect ImageFrame(PinRect outerFrame) => new(
        outerFrame.X + Thickness,
        outerFrame.Y + Thickness,
        Math.Max(1, outerFrame.Width - Thickness * 2),
        Math.Max(1, outerFrame.Height - Thickness * 2));

    public static byte[] ComposePremultipliedBgra(
        ReadOnlySpan<byte> imagePixels,
        int imageWidth,
        int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageWidth));
        int sourceLength = checked(imageWidth * imageHeight * 4);
        if (imagePixels.Length != sourceLength)
            throw new ArgumentException(
                $"Expected {sourceLength} BGRA bytes, received {imagePixels.Length}.",
                nameof(imagePixels));

        int outerWidth = checked(imageWidth + Thickness * 2);
        int outerHeight = checked(imageHeight + Thickness * 2);
        var output = new byte[checked(outerWidth * outerHeight * 4)];

        // Opaque Index accent blue (#69A3FF), already valid premultiplied BGRA.
        for (int y = 0; y < outerHeight; y++)
        {
            for (int x = 0; x < outerWidth; x++)
            {
                if (x >= Thickness && x < outerWidth - Thickness
                    && y >= Thickness && y < outerHeight - Thickness)
                    continue;
                int offset = (y * outerWidth + x) * 4;
                output[offset] = 0xFF;
                output[offset + 1] = 0xA3;
                output[offset + 2] = 0x69;
                output[offset + 3] = 0xFF;
            }
        }

        int sourceStride = checked(imageWidth * 4);
        int destinationStride = checked(outerWidth * 4);
        for (int y = 0; y < imageHeight; y++)
        {
            imagePixels.Slice(y * sourceStride, sourceStride).CopyTo(
                output.AsSpan(
                    (y + Thickness) * destinationStride + Thickness * 4,
                    sourceStride));
        }
        return output;
    }
}
