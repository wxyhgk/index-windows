using System.Runtime.InteropServices;
using Index.Capture;
using SkiaSharp;

namespace Index.Platform;

/// <summary>
/// Platform adapter that decodes frozen PNG frames into the compact luminance input consumed by
/// the platform-independent edge detector. Decode/index failures only disable pixel snapping;
/// they must never prevent the capture overlay from opening.
/// </summary>
public static class FrozenPixelEdgeDetectorFactory
{
    public static IReadOnlyDictionary<string, FrozenPixelEdgeDetector> CreateAll(
        IReadOnlyList<DisplaySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var result = new Dictionary<string, FrozenPixelEdgeDetector>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var detector = TryCreate(snapshot);
            if (detector is not null)
                result[snapshot.DisplayId] = detector;
        }
        return result;
    }

    public static FrozenPixelEdgeDetector? TryCreate(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            using var decoded = SKBitmap.Decode(snapshot.PngData);
            if (decoded is null || decoded.Width < 16 || decoded.Height < 16)
                return null;
            using var bitmap = decoded.Copy(SKColorType.Bgra8888);
            if (bitmap is null)
                return null;

            var bgra = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), bgra, 0, bgra.Length);
            var luminance = new byte[checked(bitmap.Width * bitmap.Height)];
            for (int y = 0; y < bitmap.Height; y++)
            {
                int sourceRow = y * bitmap.RowBytes;
                int targetRow = y * bitmap.Width;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int source = sourceRow + x * 4;
                    // Integer Rec. 601 luma; channel order is BGRA8888.
                    luminance[targetRow + x] = (byte)(
                        (77 * bgra[source + 2]
                         + 150 * bgra[source + 1]
                         + 29 * bgra[source]) >> 8);
                }
            }

            return new FrozenPixelEdgeDetector(
                new LuminanceBuffer(bitmap.Width, bitmap.Height, bitmap.Width, luminance));
        }
        catch
        {
            return null;
        }
    }
}
