namespace Index.Capture;

/// <summary>
/// A read-only 8-bit luminance plane. Coordinates and stride are expressed in pixels.
/// The buffer may contain padding at the end of each row.
/// </summary>
public readonly struct LuminanceBuffer
{
    public LuminanceBuffer(int width, int height, int stride, ReadOnlyMemory<byte> pixels)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (stride < width)
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride must cover every pixel in a row.");

        long requiredLength = (long)stride * (height - 1) + width;
        if (pixels.Length < requiredLength)
            throw new ArgumentException("The pixel buffer is smaller than its declared dimensions.", nameof(pixels));

        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
}

/// <summary>Tuning values for frozen-image region detection.</summary>
public sealed record FrozenPixelEdgeOptions
{
    /// <summary>Gradient required for a ray to nominate an edge.</summary>
    public byte StrongGradient { get; init; } = 24;

    /// <summary>Gradient used when checking whether a nominated edge continues.</summary>
    public byte WeakGradient { get; init; } = 10;

    /// <summary>Minimum returned dimensions in source pixels.</summary>
    public int MinimumWidth { get; init; } = 16;
    public int MinimumHeight { get; init; } = 16;

    /// <summary>Parallel pixels sampled around the pointer ray on either side.</summary>
    public int RayHalfWidth { get; init; } = 2;

    /// <summary>Maximum strong edges retained in each direction.</summary>
    public int EdgesPerDirection { get; init; } = 10;

    /// <summary>Maximum axis intervals combined during rectangle validation.</summary>
    public int IntervalsPerAxis { get; init; } = 5;

    /// <summary>Required mean continuity across all four candidate borders.</summary>
    public double MinimumBorderCoverage { get; init; } = 0.42;

    /// <summary>
    /// Required continuity for every visible (non-image-boundary) border. This prevents a
    /// short glyph stroke from pairing with image boundaries to masquerade as a rectangle.
    /// </summary>
    public double MinimumVisibleBorderCoverage { get; init; } = 0.40;

    internal void Validate()
    {
        if (StrongGradient == 0)
            throw new ArgumentOutOfRangeException(nameof(StrongGradient));
        if (WeakGradient == 0 || WeakGradient > StrongGradient)
            throw new ArgumentOutOfRangeException(nameof(WeakGradient));
        if (MinimumWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumWidth));
        if (MinimumHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumHeight));
        if (RayHalfWidth < 0 || RayHalfWidth > 8)
            throw new ArgumentOutOfRangeException(nameof(RayHalfWidth));
        if (EdgesPerDirection < 1 || EdgesPerDirection > 32)
            throw new ArgumentOutOfRangeException(nameof(EdgesPerDirection));
        if (IntervalsPerAxis < 1 || IntervalsPerAxis > 16)
            throw new ArgumentOutOfRangeException(nameof(IntervalsPerAxis));
        if (MinimumBorderCoverage is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumBorderCoverage));
        if (MinimumVisibleBorderCoverage is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumVisibleBorderCoverage));
    }
}

/// <summary>
/// Platform-independent fallback for snapping a capture selection to visible pixel edges.
/// It builds two compact gradient planes once per frozen frame. Pointer queries then inspect
/// only one horizontal and one vertical ray plus a bounded number of candidate borders; they
/// never rescan the full image.
/// </summary>
public sealed class FrozenPixelEdgeDetector
{
    private readonly int _width;
    private readonly int _height;
    private readonly FrozenPixelEdgeOptions _options;
    private readonly byte[] _verticalGradient;
    private readonly byte[] _horizontalGradient;

    public FrozenPixelEdgeDetector(LuminanceBuffer luminance, FrozenPixelEdgeOptions? options = null)
    {
        _options = options ?? new FrozenPixelEdgeOptions();
        _options.Validate();
        if (_options.MinimumWidth > luminance.Width)
            throw new ArgumentOutOfRangeException(nameof(options), "Minimum width exceeds the image width.");
        if (_options.MinimumHeight > luminance.Height)
            throw new ArgumentOutOfRangeException(nameof(options), "Minimum height exceeds the image height.");

        _width = luminance.Width;
        _height = luminance.Height;
        _verticalGradient = new byte[checked(_width * _height)];
        _horizontalGradient = new byte[checked(_width * _height)];
        BuildGradientIndex(luminance);
    }

    public int Width => _width;
    public int Height => _height;

    /// <summary>
    /// Finds the smallest well-supported rectangular region containing <paramref name="pointer"/>.
    /// The returned <see cref="SelectionRect"/> uses source-pixel coordinates; the UI host is
    /// responsible for converting it to logical display coordinates.
    /// </summary>
    public SelectionRect? FindContainingRegion(SelectionPoint pointer)
    {
        if (!double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y))
            return null;

        int px = (int)Math.Floor(pointer.X);
        int py = (int)Math.Floor(pointer.Y);
        if (px < 0 || py < 0 || px >= _width || py >= _height)
            return null;

        Span<EdgeCandidate> left = stackalloc EdgeCandidate[32];
        Span<EdgeCandidate> right = stackalloc EdgeCandidate[32];
        Span<EdgeCandidate> top = stackalloc EdgeCandidate[32];
        Span<EdgeCandidate> bottom = stackalloc EdgeCandidate[32];

        int leftCount = CollectVerticalEdges(px, py, -1, left);
        int rightCount = CollectVerticalEdges(px + 1, py, 1, right);
        int topCount = CollectHorizontalEdges(px, py, -1, top);
        int bottomCount = CollectHorizontalEdges(px, py + 1, 1, bottom);

        Span<AxisInterval> horizontal = stackalloc AxisInterval[16];
        Span<AxisInterval> vertical = stackalloc AxisInterval[16];
        int horizontalCount = BuildIntervals(
            left[..leftCount], right[..rightCount], _options.MinimumWidth, horizontal);
        int verticalCount = BuildIntervals(
            top[..topCount], bottom[..bottomCount], _options.MinimumHeight, vertical);

        SelectionRect? best = null;
        double bestScore = double.PositiveInfinity;
        for (int y = 0; y < verticalCount; y++)
        {
            for (int x = 0; x < horizontalCount; x++)
            {
                AxisInterval h = horizontal[x];
                AxisInterval v = vertical[y];
                int actualBorders = (h.Start.IsImageBoundary ? 0 : 1)
                    + (h.End.IsImageBoundary ? 0 : 1)
                    + (v.Start.IsImageBoundary ? 0 : 1)
                    + (v.End.IsImageBoundary ? 0 : 1);
                if (actualBorders < 2)
                    continue;

                double leftCoverage = VerticalCoverage(h.Start.Position, v.Start.Position, v.End.Position, h.Start.IsImageBoundary);
                double rightCoverage = VerticalCoverage(h.End.Position, v.Start.Position, v.End.Position, h.End.IsImageBoundary);
                double topCoverage = HorizontalCoverage(v.Start.Position, h.Start.Position, h.End.Position, v.Start.IsImageBoundary);
                double bottomCoverage = HorizontalCoverage(v.End.Position, h.Start.Position, h.End.Position, v.End.IsImageBoundary);
                if (!HasVisibleBorderCoverage(h.Start, leftCoverage)
                    || !HasVisibleBorderCoverage(h.End, rightCoverage)
                    || !HasVisibleBorderCoverage(v.Start, topCoverage)
                    || !HasVisibleBorderCoverage(v.End, bottomCoverage))
                    continue;
                double coverage = (leftCoverage + rightCoverage + topCoverage + bottomCoverage) / 4;
                if (coverage < _options.MinimumBorderCoverage)
                    continue;

                double width = h.End.Position - h.Start.Position;
                double height = v.End.Position - v.Start.Position;
                double area = width * height;
                // Prefer compact containing regions, but allow a more coherent outer rectangle
                // to beat a slightly smaller region made from short text or icon strokes.
                double score = area * (1.35 - 0.35 * coverage);
                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = new SelectionRect(h.Start.Position, v.Start.Position, width, height);
            }
        }

        return best;
    }

    private bool HasVisibleBorderCoverage(EdgeCandidate edge, double coverage) =>
        edge.IsImageBoundary || coverage >= _options.MinimumVisibleBorderCoverage;

    private void BuildGradientIndex(LuminanceBuffer luminance)
    {
        ReadOnlySpan<byte> source = luminance.Pixels.Span;
        for (int y = 0; y < _height; y++)
        {
            int sourceRow = y * luminance.Stride;
            int targetRow = y * _width;
            for (int x = 1; x < _width; x++)
            {
                _verticalGradient[targetRow + x] = (byte)Math.Abs(
                    source[sourceRow + x] - source[sourceRow + x - 1]);
            }
        }

        for (int y = 1; y < _height; y++)
        {
            int sourceRow = y * luminance.Stride;
            int previousRow = sourceRow - luminance.Stride;
            int targetRow = y * _width;
            for (int x = 0; x < _width; x++)
            {
                _horizontalGradient[targetRow + x] = (byte)Math.Abs(
                    source[sourceRow + x] - source[previousRow + x]);
            }
        }
    }

    private int CollectVerticalEdges(int start, int y, int direction, Span<EdgeCandidate> destination)
    {
        int count = 0;
        int lastAccepted = int.MinValue;
        int limit = Math.Min(destination.Length, _options.EdgesPerDirection);
        for (int x = start; x > 0 && x < _width && count < limit; x += direction)
        {
            int score = VerticalRayScore(x, y);
            if (score < _options.StrongGradient || score < VerticalRayScore(x - direction, y))
                continue;
            if (lastAccepted != int.MinValue && Math.Abs(x - lastAccepted) <= 2)
                continue;

            destination[count++] = new EdgeCandidate(x, false);
            lastAccepted = x;
        }

        int boundary = direction < 0 ? 0 : _width;
        if (count < destination.Length)
            destination[count++] = new EdgeCandidate(boundary, true);
        return count;
    }

    private int CollectHorizontalEdges(int x, int start, int direction, Span<EdgeCandidate> destination)
    {
        int count = 0;
        int lastAccepted = int.MinValue;
        int limit = Math.Min(destination.Length, _options.EdgesPerDirection);
        for (int y = start; y > 0 && y < _height && count < limit; y += direction)
        {
            int score = HorizontalRayScore(x, y);
            if (score < _options.StrongGradient || score < HorizontalRayScore(x, y - direction))
                continue;
            if (lastAccepted != int.MinValue && Math.Abs(y - lastAccepted) <= 2)
                continue;

            destination[count++] = new EdgeCandidate(y, false);
            lastAccepted = y;
        }

        int boundary = direction < 0 ? 0 : _height;
        if (count < destination.Length)
            destination[count++] = new EdgeCandidate(boundary, true);
        return count;
    }

    private int VerticalRayScore(int x, int y)
    {
        if (x <= 0 || x >= _width)
            return 0;
        int first = Math.Max(0, y - _options.RayHalfWidth);
        int last = Math.Min(_height - 1, y + _options.RayHalfWidth);
        int sum = 0;
        for (int sample = first; sample <= last; sample++)
            sum += _verticalGradient[sample * _width + x];
        return sum / (last - first + 1);
    }

    private int HorizontalRayScore(int x, int y)
    {
        if (y <= 0 || y >= _height)
            return 0;
        int first = Math.Max(0, x - _options.RayHalfWidth);
        int last = Math.Min(_width - 1, x + _options.RayHalfWidth);
        int sum = 0;
        int row = y * _width;
        for (int sample = first; sample <= last; sample++)
            sum += _horizontalGradient[row + sample];
        return sum / (last - first + 1);
    }

    private int BuildIntervals(
        ReadOnlySpan<EdgeCandidate> starts,
        ReadOnlySpan<EdgeCandidate> ends,
        int minimumLength,
        Span<AxisInterval> destination)
    {
        int limit = Math.Min(destination.Length, _options.IntervalsPerAxis);
        int regularLimit = Math.Max(0, limit - 1);
        int count = 0;
        for (int startIndex = 0; startIndex < starts.Length; startIndex++)
        {
            for (int endIndex = 0; endIndex < ends.Length; endIndex++)
            {
                if (ends[endIndex].Position - starts[startIndex].Position < minimumLength)
                    continue;

                var candidate = new AxisInterval(starts[startIndex], ends[endIndex]);
                int insertion = count;
                while (insertion > 0 && destination[insertion - 1].Length > candidate.Length)
                    insertion--;

                if (insertion >= regularLimit)
                    continue;
                int upper = Math.Min(count, regularLimit - 1);
                for (int move = upper; move > insertion; move--)
                    destination[move] = destination[move - 1];
                destination[insertion] = candidate;
                if (count < regularLimit)
                    count++;
            }
        }

        // Short strokes close to the pointer can fill the compact-candidate budget. Preserve
        // one outer visible interval so its long, coherent borders still get validated.
        if (limit > 0)
        {
            var outer = new AxisInterval(OutermostVisibleOrBoundary(starts), OutermostVisibleOrBoundary(ends));
            bool duplicate = false;
            for (int index = 0; index < count; index++)
                duplicate |= destination[index].Equals(outer);
            if (!duplicate && outer.Length >= minimumLength)
                destination[count++] = outer;
        }

        return count;
    }

    private static EdgeCandidate OutermostVisibleOrBoundary(ReadOnlySpan<EdgeCandidate> edges)
    {
        for (int index = edges.Length - 1; index >= 0; index--)
        {
            if (!edges[index].IsImageBoundary)
                return edges[index];
        }
        return edges[^1];
    }

    private double VerticalCoverage(int x, int top, int bottom, bool imageBoundary)
    {
        if (imageBoundary)
            return 1;
        int supported = 0;
        int samples = 0;
        int step = Math.Max(1, (bottom - top) / 96);
        for (int y = top; y < bottom; y += step)
        {
            samples++;
            if (MaxVerticalGradient(x, y) >= _options.WeakGradient)
                supported++;
        }
        return samples == 0 ? 0 : supported / (double)samples;
    }

    private double HorizontalCoverage(int y, int left, int right, bool imageBoundary)
    {
        if (imageBoundary)
            return 1;
        int supported = 0;
        int samples = 0;
        int step = Math.Max(1, (right - left) / 96);
        for (int x = left; x < right; x += step)
        {
            samples++;
            if (MaxHorizontalGradient(x, y) >= _options.WeakGradient)
                supported++;
        }
        return samples == 0 ? 0 : supported / (double)samples;
    }

    private byte MaxVerticalGradient(int x, int y)
    {
        byte maximum = 0;
        int start = Math.Max(1, x - 1);
        int end = Math.Min(_width - 1, x + 1);
        int row = y * _width;
        for (int sample = start; sample <= end; sample++)
            maximum = Math.Max(maximum, _verticalGradient[row + sample]);
        return maximum;
    }

    private byte MaxHorizontalGradient(int x, int y)
    {
        byte maximum = 0;
        int start = Math.Max(1, y - 1);
        int end = Math.Min(_height - 1, y + 1);
        for (int sample = start; sample <= end; sample++)
            maximum = Math.Max(maximum, _horizontalGradient[sample * _width + x]);
        return maximum;
    }

    private readonly record struct EdgeCandidate(int Position, bool IsImageBoundary);

    private readonly record struct AxisInterval(EdgeCandidate Start, EdgeCandidate End)
    {
        public int Length => End.Position - Start.Position;
    }
}
