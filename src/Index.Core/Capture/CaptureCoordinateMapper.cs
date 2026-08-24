namespace Index.Capture;

/// <summary>Integer crop bounds in a frozen display's physical-pixel space.</summary>
public readonly record struct PixelCaptureRect(int X, int Y, int Width, int Height);

/// <summary>Integer size in a frozen display's physical-pixel space.</summary>
public readonly record struct PixelCaptureSize(int Width, int Height);

/// <summary>
/// Converts between the overlay's logical coordinate space and the frozen bitmap's physical
/// pixels. Keeping one mapper per operation prevents selection, labels, and edge detection from
/// silently using different scale calculations.
/// </summary>
public readonly record struct CaptureCoordinateMapper
{
    public CaptureCoordinateMapper(
        int pixelWidth,
        int pixelHeight,
        double logicalWidth,
        double logicalHeight,
        double fallbackDpiScale)
    {
        if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        if (!double.IsFinite(logicalWidth) || logicalWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        if (!double.IsFinite(logicalHeight) || logicalHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        if (!double.IsFinite(fallbackDpiScale) || fallbackDpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(fallbackDpiScale));

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        FallbackDpiScale = fallbackDpiScale;
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double LogicalWidth { get; }
    public double LogicalHeight { get; }
    public double FallbackDpiScale { get; }

    public double ScaleX => LogicalWidth > 0 ? PixelWidth / LogicalWidth : FallbackDpiScale;
    public double ScaleY => LogicalHeight > 0 ? PixelHeight / LogicalHeight : FallbackDpiScale;

    public SelectionPoint LogicalToPixel(SelectionPoint point) =>
        new(point.X * ScaleX, point.Y * ScaleY);

    public SelectionRect PixelToLogical(SelectionRect rect) =>
        new(rect.X / ScaleX, rect.Y / ScaleY, rect.Width / ScaleX, rect.Height / ScaleY);

    /// <summary>Matches the crop path's historical truncation semantics.</summary>
    public PixelCaptureRect ToCropRect(SelectionRect rect) => new(
        (int)(rect.X * ScaleX),
        (int)(rect.Y * ScaleY),
        (int)(rect.Width * ScaleX),
        (int)(rect.Height * ScaleY));

    /// <summary>Matches the size label's historical nearest-pixel display semantics.</summary>
    public PixelCaptureSize ToDisplaySize(SelectionRect rect) => new(
        Math.Max(1, (int)Math.Round(rect.Width * ScaleX)),
        Math.Max(1, (int)Math.Round(rect.Height * ScaleY)));
}
