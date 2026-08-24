using Index.Actions;
using Index.Annotation;
using Index.Capture;
using Index.Platform.Clipboard;
using SkiaSharp;

namespace Index.Tests;

public sealed class CopyActionTests
{
    [Fact]
    public async Task PerformAsync_WritesRenderedPngToClipboardBoundary()
    {
        var clipboard = new RecordingClipboardWriter();
        var layers = new Layers<ImageSpace>();
        layers.Append(new Layer(
            LayerKind.Rect,
            new LRect(1, 1, 5, 5),
            LColor.Red,
            1));
        byte[] basePng = MakePng();
        var artifact = new CaptureArtifact(basePng, layers);

        await new CopyAction(clipboard).PerformAsync(new CaptureContext
        {
            Artifact = artifact
        });

        Assert.NotNull(clipboard.Png);
        using var decoded = SKBitmap.Decode(clipboard.Png);
        Assert.NotNull(decoded);
        Assert.Equal(8, decoded.Width);
        Assert.Equal(8, decoded.Height);
        Assert.False(basePng.SequenceEqual(clipboard.Png!));
    }

    [Fact]
    public void Descriptor_IsVisibleInCaptureAndPinnedScopes()
    {
        var descriptor = new CopyAction(new RecordingClipboardWriter()).Descriptor;

        Assert.Equal(CaptureActionIds.Copy, descriptor.Id);
        Assert.Contains(CaptureActionScope.Capture, descriptor.Scopes);
        Assert.Contains(CaptureActionScope.Pinned, descriptor.Scopes);
    }

    private static byte[] MakePng()
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        surface.Canvas.Clear(SKColors.White);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class RecordingClipboardWriter : IClipboardWriter
    {
        public byte[]? Png { get; private set; }

        public void WriteText(string text) { }

        public ValueTask WritePngAsync(
            ReadOnlyMemory<byte> png,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Png = png.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteFilesAsync(
            IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
