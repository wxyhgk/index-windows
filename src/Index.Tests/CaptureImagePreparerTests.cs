using Index.Annotation;
using Index.Capture;
using Index.Platform.Capture;
using SkiaSharp;
using System.Runtime.Versioning;

namespace Index.Tests;

[SupportedOSPlatform("windows6.1")]
public sealed class CaptureImagePreparerTests
{
    private readonly WindowsCaptureImagePreparer _preparer = new();

    [Fact]
    public async Task PrepareFrozen_CropsSelectionAndPreservesLayers()
    {
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Rect,
            new LRect(0, 0, 1, 1),
            LColor.Red,
            1));
        var selection = Selection(2, 1, 3, 2, layers);

        var prepared = _preparer.PrepareFrozen(selection, MakePng(8, 6));

        using var decoded = SKBitmap.Decode(prepared.BasePng);
        Assert.Equal(3, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(SKColors.White, decoded.GetPixel(0, 0));

        var rendered = await prepared.Artifact.GetRenderedPngAsync();
        Assert.False(prepared.BasePng.SequenceEqual(rendered.ToArray()));
    }

    [Fact]
    public void PrepareFrozen_ClampsSelectionToFrozenImageBounds()
    {
        var prepared = _preparer.PrepareFrozen(
            Selection(3, 2, 50, 50),
            MakePng(5, 4));

        using var decoded = SKBitmap.Decode(prepared.BasePng);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(2, decoded.Height);
    }

    [Fact]
    public void TryPrepareDirect_AcceptsTwoPixelCompositorToleranceWithoutCropping()
    {
        var directPng = MakePng(102, 78);

        var prepared = _preparer.TryPrepareDirect(
            Selection(0, 0, 100, 80),
            directPng);

        Assert.NotNull(prepared);
        Assert.Same(directPng, prepared.BasePng);
    }

    [Theory]
    [InlineData(103, 80)]
    [InlineData(100, 77)]
    public void TryPrepareDirect_RejectsDimensionMismatchBeyondTolerance(int width, int height)
    {
        var prepared = _preparer.TryPrepareDirect(
            Selection(0, 0, 100, 80),
            MakePng(width, height));

        Assert.Null(prepared);
    }

    [Fact]
    public void TryPrepareDirect_RejectsInvalidPng()
    {
        var prepared = _preparer.TryPrepareDirect(
            Selection(0, 0, 100, 80),
            [1, 2, 3]);

        Assert.Null(prepared);
    }

    private static CaptureSelection Selection(
        int x,
        int y,
        int width,
        int height,
        Layers<ImageSpace>? layers = null)
        => new()
        {
            Display = new CaptureDisplayIdentity("display", 0, 0, 200, 200, 1),
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Layers = layers ?? new Layers<ImageSpace>()
        };

    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
