using System.Buffers.Binary;
using Index.Capture;

namespace Index.Tests;

public sealed class PngImageHeaderTests
{
    [Fact]
    public void ReadsDimensionsFromIhdr()
    {
        var png = Header(3840, 2160);

        var dimensions = PngImageHeader.Read(png);

        Assert.Equal(new PngImageDimensions(3840, 2160), dimensions);
    }

    [Fact]
    public void RejectsNonPngAndInvalidDimensions()
    {
        Assert.Throws<InvalidDataException>(() => PngImageHeader.Read([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => PngImageHeader.Read(Header(0, 2160)));
    }

    private static byte[] Header(int width, int height)
    {
        byte[] png = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), height);
        return png;
    }
}
