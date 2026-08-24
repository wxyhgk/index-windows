namespace Index.Pin;

public readonly record struct PinRect(double X, double Y, double Width, double Height)
{
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
}

/// <summary>Pure aspect-preserving pin sizing shared by the WinUI host and tests.</summary>
public static class PinWindowGeometry
{
    public const double MinimumZoom = 0.15;
    public const double MaximumZoom = 4.0;
    public const double WheelStep = 1.1;
    private const double WorkAreaInset = 12;

    public static double FitZoom(double naturalWidth, double naturalHeight, PinRect workArea)
    {
        ValidateSize(naturalWidth, naturalHeight);
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));

        double availableWidth = Math.Max(1, workArea.Width - WorkAreaInset * 2);
        double availableHeight = Math.Max(1, workArea.Height - WorkAreaInset * 2);
        return Math.Clamp(
            Math.Min(1, Math.Min(availableWidth / naturalWidth, availableHeight / naturalHeight)),
            MinimumZoom,
            MaximumZoom);
    }

    public static PinRect InitialFrame(PinRect naturalFrame, PinRect workArea)
    {
        ValidateSize(naturalFrame.Width, naturalFrame.Height);
        double zoom = FitZoom(naturalFrame.Width, naturalFrame.Height, workArea);
        double width = naturalFrame.Width * zoom;
        double height = naturalFrame.Height * zoom;
        double x = ClampOrigin(naturalFrame.MidX - width / 2, width, workArea.X, workArea.Width);
        double y = ClampOrigin(naturalFrame.MidY - height / 2, height, workArea.Y, workArea.Height);
        return new PinRect(x, y, width, height);
    }

    public static PinRect ZoomAroundCenter(
        PinRect current,
        double naturalWidth,
        double naturalHeight,
        double zoom,
        PinRect workArea)
    {
        ValidateSize(naturalWidth, naturalHeight);
        double clampedZoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        double width = naturalWidth * clampedZoom;
        double height = naturalHeight * clampedZoom;
        double x = ClampOrigin(current.MidX - width / 2, width, workArea.X, workArea.Width);
        double y = ClampOrigin(current.MidY - height / 2, height, workArea.Y, workArea.Height);
        return new PinRect(x, y, width, height);
    }

    private static double ClampOrigin(double origin, double size, double areaOrigin, double areaSize)
    {
        if (size >= areaSize)
            return areaOrigin + (areaSize - size) / 2;
        return Math.Clamp(origin, areaOrigin, areaOrigin + areaSize - size);
    }

    private static void ValidateSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
    }
}

/// <summary>Places the independent pin toolbar without changing the image window's layout.</summary>
public static class PinToolbarGeometry
{
    public const double Gap = 8;
    private const double EdgeInset = 8;

    public static PinRect Place(
        PinRect imageFrame,
        double toolbarWidth,
        double toolbarHeight,
        PinRect workArea)
    {
        if (toolbarWidth <= 0 || toolbarHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(toolbarWidth));

        double x = Math.Clamp(
            imageFrame.MidX - toolbarWidth / 2,
            workArea.X + EdgeInset,
            Math.Max(workArea.X + EdgeInset, workArea.X + workArea.Width - EdgeInset - toolbarWidth));
        bool fitsBelow = imageFrame.Y + imageFrame.Height + Gap + toolbarHeight
            <= workArea.Y + workArea.Height - EdgeInset;
        double y = fitsBelow
            ? imageFrame.Y + imageFrame.Height + Gap
            : Math.Max(workArea.Y + EdgeInset, imageFrame.Y - Gap - toolbarHeight);
        return new PinRect(x, y, toolbarWidth, toolbarHeight);
    }
}
