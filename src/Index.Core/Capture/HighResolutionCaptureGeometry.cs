using Index.Annotation;
using Index.Platform;

namespace Index.Capture;

public readonly record struct HighResolutionSelectionMapping(
    SourceWindowBounds GlobalBounds,
    SourceWindowBounds ImageBounds,
    double ScaleX,
    double ScaleY);

/// <summary>Pure geometry for mapping the first screen selection into a dense window frame.</summary>
public static class HighResolutionCaptureGeometry
{
    public static HighResolutionSelectionMapping MapFromOriginalWindow(
        SourceWindowBounds requestedGlobalBounds,
        SourceWindowBounds originalWindowBounds,
        int imageWidth,
        int imageHeight)
    {
        if (requestedGlobalBounds.Width <= 0 || requestedGlobalBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedGlobalBounds));
        if (originalWindowBounds.Width <= 0 || originalWindowBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(originalWindowBounds));
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));

        var globalBounds = new SourceWindowBounds(
            Math.Max(requestedGlobalBounds.Left, originalWindowBounds.Left),
            Math.Max(requestedGlobalBounds.Top, originalWindowBounds.Top),
            Math.Min(requestedGlobalBounds.Right, originalWindowBounds.Right),
            Math.Min(requestedGlobalBounds.Bottom, originalWindowBounds.Bottom));
        if (globalBounds.Width <= 0 || globalBounds.Height <= 0)
        {
            throw new InvalidOperationException(
                "The selected region no longer intersects the captured window.");
        }

        double scaleX = imageWidth / (double)originalWindowBounds.Width;
        double scaleY = imageHeight / (double)originalWindowBounds.Height;
        int left = Math.Clamp(
            (int)Math.Floor((globalBounds.Left - originalWindowBounds.Left) * scaleX),
            0,
            imageWidth - 1);
        int top = Math.Clamp(
            (int)Math.Floor((globalBounds.Top - originalWindowBounds.Top) * scaleY),
            0,
            imageHeight - 1);
        int right = Math.Clamp(
            (int)Math.Ceiling((globalBounds.Right - originalWindowBounds.Left) * scaleX),
            left + 1,
            imageWidth);
        int bottom = Math.Clamp(
            (int)Math.Ceiling((globalBounds.Bottom - originalWindowBounds.Top) * scaleY),
            top + 1,
            imageHeight);
        return new HighResolutionSelectionMapping(
            globalBounds,
            new SourceWindowBounds(left, top, right, bottom),
            scaleX,
            scaleY);
    }

    public static Layers<ImageSpace> MapLayersFromOriginalSelection(
        Layers<ImageSpace> layers,
        SourceWindowBounds requestedGlobalBounds,
        HighResolutionSelectionMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (requestedGlobalBounds.Width <= 0 || requestedGlobalBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedGlobalBounds));
        if (!double.IsFinite(mapping.ScaleX) || mapping.ScaleX <= 0
            || !double.IsFinite(mapping.ScaleY) || mapping.ScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapping));
        }

        int sourceOffsetX = mapping.GlobalBounds.Left - requestedGlobalBounds.Left;
        int sourceOffsetY = mapping.GlobalBounds.Top - requestedGlobalBounds.Top;
        double visualScale = Math.Sqrt(mapping.ScaleX * mapping.ScaleY);
        var result = new Layers<ImageSpace>();
        foreach (var layer in layers.Elements)
        {
            result.Append(layer with
            {
                Rect = new LRect(
                    (layer.Rect.X - sourceOffsetX) * mapping.ScaleX,
                    (layer.Rect.Y - sourceOffsetY) * mapping.ScaleY,
                    layer.Rect.W * mapping.ScaleX,
                    layer.Rect.H * mapping.ScaleY),
                LineWidth = layer.LineWidth * visualScale,
                FontSize = layer.FontSize * visualScale
            });
        }
        return result;
    }
}
