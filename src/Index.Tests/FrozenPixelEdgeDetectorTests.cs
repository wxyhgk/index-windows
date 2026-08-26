using Index.Capture;

namespace Index.Tests;

public sealed class FrozenPixelEdgeDetectorTests
{
    [Fact]
    public void FindsCleanRectangleContainingPointer()
    {
        var image = Image(100, 80, background: 20);
        Fill(image, 100, 20, 15, 70, 60, 180);

        var detector = Detector(image, 100, 80);

        Assert.Equal(new SelectionRect(20, 15, 50, 45), detector.FindContainingRegion(new SelectionPoint(45, 35)));
    }

    [Fact]
    public void UsesStrideAndIgnoresPaddingBytes()
    {
        const int width = 64;
        const int height = 48;
        const int stride = 72;
        var image = Image(stride, height, background: 33);
        Fill(image, stride, 8, 6, 54, 40, 210);
        for (int y = 0; y < height; y++)
            Array.Fill(image, (byte)(y % 2 == 0 ? 0 : 255), y * stride + width, stride - width);

        var detector = new FrozenPixelEdgeDetector(new LuminanceBuffer(width, height, stride, image));

        Assert.Equal(new SelectionRect(8, 6, 46, 34), detector.FindContainingRegion(new SelectionPoint(30, 20)));
    }

    [Fact]
    public void RejectsUniformImageInsteadOfReturningWholeScreen()
    {
        var detector = Detector(Image(80, 60, 90), 80, 60);

        Assert.Null(detector.FindContainingRegion(new SelectionPoint(40, 30)));
    }

    [Fact]
    public void ShortInteriorStrokesDoNotBeatCoherentOuterBorder()
    {
        var image = Image(120, 90, background: 10);
        Fill(image, 120, 10, 10, 110, 80, 150);
        // A high-contrast cross intersects both pointer rays but is too short to be a rectangle.
        Fill(image, 120, 54, 35, 57, 56, 245);
        Fill(image, 120, 48, 44, 68, 47, 245);

        var detector = Detector(image, 120, 90);

        Assert.Equal(new SelectionRect(10, 10, 100, 70), detector.FindContainingRegion(new SelectionPoint(55, 45)));
    }

    [Fact]
    public void SupportsRegionTouchingImageBoundaryWhenOtherEdgesAreVisible()
    {
        var image = Image(90, 70, background: 15);
        Fill(image, 90, 0, 8, 65, 58, 190);

        var detector = Detector(image, 90, 70);

        Assert.Equal(new SelectionRect(0, 8, 65, 50), detector.FindContainingRegion(new SelectionPoint(25, 30)));
    }

    [Fact]
    public void ChoosesSmallestCoherentNestedRegion()
    {
        var image = Image(140, 100, background: 5);
        Fill(image, 140, 8, 8, 132, 92, 100);
        Fill(image, 140, 35, 25, 105, 75, 220);

        var detector = Detector(image, 140, 100);

        Assert.Equal(new SelectionRect(35, 25, 70, 50), detector.FindContainingRegion(new SelectionPoint(60, 45)));
    }

    [Fact]
    public void GradientIndexIsIndependentFromLaterSourceMutation()
    {
        var image = Image(80, 60, background: 10);
        Fill(image, 80, 12, 10, 68, 50, 200);
        var detector = Detector(image, 80, 60);
        Array.Fill(image, (byte)10);

        Assert.Equal(new SelectionRect(12, 10, 56, 40), detector.FindContainingRegion(new SelectionPoint(30, 30)));
    }

    [Fact]
    public void ReturnsNullForPointerOutsideImage()
    {
        var detector = Detector(Image(40, 30, 0), 40, 30, minimum: 8);

        Assert.Null(detector.FindContainingRegion(new SelectionPoint(-1, 10)));
        Assert.Null(detector.FindContainingRegion(new SelectionPoint(40, 10)));
        Assert.Null(detector.FindContainingRegion(new SelectionPoint(double.NaN, 10)));
    }

    [Fact]
    public void HonorsConfiguredMinimumSize()
    {
        var image = Image(80, 60, background: 0);
        Fill(image, 80, 30, 20, 42, 31, 255);
        var detector = Detector(image, 80, 60, minimum: 20);

        Assert.Null(detector.FindContainingRegion(new SelectionPoint(35, 25)));
    }

    [Fact]
    public void ValidatesBufferShapeAndOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuminanceBuffer(0, 1, 1, new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LuminanceBuffer(2, 1, 1, new byte[2]));
        Assert.Throws<ArgumentException>(() => new LuminanceBuffer(2, 2, 2, new byte[3]));

        var buffer = new LuminanceBuffer(8, 8, 8, new byte[64]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrozenPixelEdgeDetector(
            buffer,
            new FrozenPixelEdgeOptions { WeakGradient = 40, StrongGradient = 20 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrozenPixelEdgeDetector(
            buffer,
            new FrozenPixelEdgeOptions { MinimumWidth = 9, MinimumHeight = 1 }));
    }

    [Fact]
    public void HonorsCancellationBeforeAllocatingGradientPlanes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var buffer = new LuminanceBuffer(64, 48, 64, new byte[64 * 48]);

        Assert.Throws<OperationCanceledException>(() => new FrozenPixelEdgeDetector(
            buffer,
            cancellationToken: cancellation.Token));
    }

    private static FrozenPixelEdgeDetector Detector(byte[] image, int width, int height, int minimum = 16) =>
        new(
            new LuminanceBuffer(width, height, width, image),
            new FrozenPixelEdgeOptions { MinimumWidth = minimum, MinimumHeight = minimum });

    private static byte[] Image(int stride, int height, byte background)
    {
        var pixels = new byte[stride * height];
        Array.Fill(pixels, background);
        return pixels;
    }

    private static void Fill(byte[] pixels, int stride, int left, int top, int right, int bottom, byte value)
    {
        for (int y = top; y < bottom; y++)
            Array.Fill(pixels, value, y * stride + left, right - left);
    }
}
