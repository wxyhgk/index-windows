using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Index.Capture;

namespace Index.Platform.Capture;

/// <summary>
/// Windows PNG decoder/cropper used by capture orchestration. System.Drawing stays behind
/// the platform boundary so the capture-domain contract remains portable.
/// </summary>
[SupportedOSPlatform("windows6.1")]
public sealed class WindowsCaptureImagePreparer : ICaptureImagePreparer
{
    public PreparedCaptureImage PrepareFrozen(
        CaptureSelection selection,
        ReadOnlyMemory<byte> frozenPng)
    {
        ArgumentNullException.ThrowIfNull(selection);

        using var stream = new MemoryStream(frozenPng.ToArray(), writable: false);
        using var full = new Bitmap(stream);

        var x = Math.Clamp(selection.X, 0, full.Width - 1);
        var y = Math.Clamp(selection.Y, 0, full.Height - 1);
        var width = Math.Clamp(selection.Width, 1, full.Width - x);
        var height = Math.Clamp(selection.Height, 1, full.Height - y);

        using var cropped = full.Clone(
            new Rectangle(x, y, width, height),
            full.PixelFormat);
        using var croppedStream = new MemoryStream();
        cropped.Save(croppedStream, ImageFormat.Png);

        var basePng = croppedStream.ToArray();
        return new PreparedCaptureImage(
            basePng,
            new CaptureArtifact(basePng, selection.Layers));
    }

    public PreparedCaptureImage? TryPrepareDirect(
        CaptureSelection selection,
        byte[] directPng)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(directPng);

        try
        {
            using var stream = new MemoryStream(directPng, writable: false);
            using var image = Image.FromStream(
                stream,
                useEmbeddedColorManagement: false,
                validateImageData: true);
            if (Math.Abs(image.Width - selection.Width) > 2
                || Math.Abs(image.Height - selection.Height) > 2)
                return null;

            return new PreparedCaptureImage(
                directPng,
                new CaptureArtifact(directPng, selection.Layers));
        }
        catch
        {
            return null;
        }
    }
}
