namespace Index.UI.Gallery;

/// <summary>
/// Inputs for stable gallery-grid geometry. <see cref="StableContainerWidth"/> must
/// come from the outer scroll container, not the viewport width after a scrollbar
/// has appeared.
/// </summary>
public readonly record struct GalleryLayoutRequest(
    double StableContainerWidth,
    double TargetCardWidth,
    double HorizontalInset = 8,
    double Spacing = 16,
    double ScrollbarReserve = 16,
    double ColumnWidthQuantum = 0,
    double MinimumColumnWidth = 1,
    double MinimumTargetFillRatio = 0.6);

/// <summary>Deterministic geometry for one responsive gallery grid.</summary>
public readonly record struct GalleryLayoutResult(
    int ColumnCount,
    double ColumnWidth,
    double AvailableWidth,
    double UsedWidth,
    double TrailingRemainder)
{
    public double WidthForColumns(int columns, double spacing)
    {
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        return columns * ColumnWidth + (columns - 1) * spacing;
    }
}

/// <summary>
/// Pure responsive-gallery layout math. Column count is derived from a stable outer
/// width and never from scrollbar-reduced content width, breaking the feedback loop
/// between item height, scrollbar visibility, and adaptive column count.
/// </summary>
public static class GalleryLayout
{
    public static GalleryLayoutResult Calculate(GalleryLayoutRequest request)
    {
        Validate(request);

        double widthInsideInsets = Math.Max(
            0,
            request.StableContainerWidth - request.HorizontalInset * 2);

        // Match the macOS grid principle: zoom chooses the discrete column count
        // from the stable outer width. The scrollbar reserve only influences the
        // width distributed to those columns, so scrollbar appearance cannot feed
        // back into this first calculation.
        int columns = Math.Max(
            1,
            (int)Math.Floor(
                (widthInsideInsets + request.Spacing)
                / (request.TargetCardWidth + request.Spacing)));

        double availableWidth = Math.Max(0, widthInsideInsets - request.ScrollbarReserve);

        // If reserving the scrollbar would make the last inferred column markedly
        // narrower than the requested zoom, explicitly reduce the count. This is a
        // deterministic decision based on the same stable width, not a layout retry.
        while (columns > 1
            && RawColumnWidth(availableWidth, columns, request.Spacing)
                < request.TargetCardWidth * request.MinimumTargetFillRatio)
        {
            columns--;
        }

        double rawColumnWidth = RawColumnWidth(availableWidth, columns, request.Spacing);
        double columnWidth = Math.Max(request.MinimumColumnWidth, rawColumnWidth);

        if (request.ColumnWidthQuantum > 0 && columnWidth > request.MinimumColumnWidth)
        {
            columnWidth = Math.Floor(columnWidth / request.ColumnWidthQuantum)
                * request.ColumnWidthQuantum;
            columnWidth = Math.Max(request.MinimumColumnWidth, columnWidth);
        }

        double usedWidth = columns * columnWidth + (columns - 1) * request.Spacing;
        double trailingRemainder = Math.Max(0, availableWidth - usedWidth);

        return new GalleryLayoutResult(
            columns,
            columnWidth,
            availableWidth,
            usedWidth,
            trailingRemainder);
    }

    /// <summary>
    /// Scales an image to a column without changing its pixel aspect ratio.
    /// Invalid image dimensions are rejected rather than silently producing NaN.
    /// </summary>
    public static double ThumbnailHeightForPixels(
        double columnWidth,
        int pixelWidth,
        int pixelHeight,
        double minimumHeight = 1,
        double maximumHeight = double.PositiveInfinity)
    {
        if (!double.IsFinite(columnWidth) || columnWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(columnWidth));
        if (pixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        if (!double.IsFinite(minimumHeight) || minimumHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));
        if (double.IsNaN(maximumHeight) || maximumHeight < minimumHeight)
            throw new ArgumentOutOfRangeException(nameof(maximumHeight));

        double height = columnWidth * pixelHeight / pixelWidth;
        return Math.Clamp(height, minimumHeight, maximumHeight);
    }

    private static double RawColumnWidth(double availableWidth, int columns, double spacing)
        => Math.Max(0, availableWidth - (columns - 1) * spacing) / columns;

    private static void Validate(GalleryLayoutRequest request)
    {
        RequireFiniteNonNegative(request.StableContainerWidth, nameof(request.StableContainerWidth));
        RequireFinitePositive(request.TargetCardWidth, nameof(request.TargetCardWidth));
        RequireFiniteNonNegative(request.HorizontalInset, nameof(request.HorizontalInset));
        RequireFiniteNonNegative(request.Spacing, nameof(request.Spacing));
        RequireFiniteNonNegative(request.ScrollbarReserve, nameof(request.ScrollbarReserve));
        RequireFiniteNonNegative(request.ColumnWidthQuantum, nameof(request.ColumnWidthQuantum));
        RequireFinitePositive(request.MinimumColumnWidth, nameof(request.MinimumColumnWidth));

        if (!double.IsFinite(request.MinimumTargetFillRatio)
            || request.MinimumTargetFillRatio <= 0
            || request.MinimumTargetFillRatio > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MinimumTargetFillRatio));
        }
    }

    private static void RequireFiniteNonNegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void RequireFinitePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
