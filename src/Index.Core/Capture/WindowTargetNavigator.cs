using Index.Platform;

namespace Index.Capture;

/// <summary>
/// Owns hover candidates and cycling for frozen window and pixel-edge targets. It never queries
/// live windows; all inputs were frozen before the overlay appeared.
/// </summary>
public sealed class WindowTargetNavigator
{
    private readonly IReadOnlyList<WindowSelectionTarget> _windowTargets;
    private readonly FrozenPixelEdgeDetector? _pixelEdgeDetector;
    private readonly CaptureDisplayIdentity _display;
    private IReadOnlyList<WindowSelectionTarget> _candidates = Array.Empty<WindowSelectionTarget>();
    private int _candidateIndex;

    public WindowTargetNavigator(
        IReadOnlyList<WindowSelectionTarget> windowTargets,
        FrozenPixelEdgeDetector? pixelEdgeDetector,
        CaptureDisplayIdentity display)
    {
        _windowTargets = windowTargets ?? throw new ArgumentNullException(nameof(windowTargets));
        _pixelEdgeDetector = pixelEdgeDetector;
        _display = display;
    }

    public WindowSelectionTarget? CurrentTarget { get; private set; }
    public int CandidateCount => _candidates.Count;

    public WindowSelectionTarget? PreviewAt(
        SelectionPoint point,
        CaptureCoordinateMapper coordinates)
    {
        var candidates = FindCandidates(point, coordinates);
        if (!HaveSameTargets(candidates, _candidates))
        {
            _candidates = candidates;
            _candidateIndex = 0;
        }

        _candidateIndex = _candidates.Count > 0
            ? Math.Clamp(_candidateIndex, 0, _candidates.Count - 1)
            : 0;
        CurrentTarget = _candidates.Count > 0 ? _candidates[_candidateIndex] : null;
        return CurrentTarget;
    }

    public WindowSelectionTarget? FindPrimary(
        SelectionPoint point,
        CaptureCoordinateMapper coordinates) =>
        FindCandidates(point, coordinates).FirstOrDefault();

    public bool HasTargetAt(SelectionPoint point, CaptureCoordinateMapper coordinates) =>
        FindCandidates(point, coordinates).Count > 0;

    public bool TryCycle(
        SelectionPoint point,
        int direction,
        CaptureCoordinateMapper coordinates,
        out WindowSelectionTarget? target)
    {
        var candidates = FindCandidates(point, coordinates);
        if (candidates.Count <= 1)
        {
            target = null;
            return false;
        }

        if (!HaveSameTargets(candidates, _candidates))
        {
            _candidates = candidates;
            _candidateIndex = 0;
        }

        int step = direction < 0 ? -1 : 1;
        _candidateIndex = (_candidateIndex + step + _candidates.Count) % _candidates.Count;
        CurrentTarget = _candidates[_candidateIndex];
        target = CurrentTarget;
        return true;
    }

    public void Reset()
    {
        _candidates = Array.Empty<WindowSelectionTarget>();
        _candidateIndex = 0;
        CurrentTarget = null;
    }

    public static bool Contains(WindowSelectionTarget target, SelectionPoint point) =>
        point.X >= target.Bounds.Left && point.X < target.Bounds.Right
        && point.Y >= target.Bounds.Top && point.Y < target.Bounds.Bottom;

    private IReadOnlyList<WindowSelectionTarget> FindCandidates(
        SelectionPoint point,
        CaptureCoordinateMapper coordinates)
    {
        var targets = _windowTargets
            .Where(target => Contains(target, point))
            .OrderBy(target => target.IsTopLevel ? 0 : 1)
            .ToList();

        var pixelTarget = FindPixelEdgeTarget(point, coordinates);
        if (pixelTarget is not null
            && !targets.Any(target => NearlyEqual(target.Bounds, pixelTarget.Bounds)))
        {
            targets.Add(pixelTarget);
        }

        return targets;
    }

    private WindowSelectionTarget? FindPixelEdgeTarget(
        SelectionPoint point,
        CaptureCoordinateMapper coordinates)
    {
        if (_pixelEdgeDetector is null)
            return null;

        var pixelBounds = _pixelEdgeDetector.FindContainingRegion(
            coordinates.LogicalToPixel(point));
        if (pixelBounds is not { } region)
            return null;

        var logicalBounds = coordinates.PixelToLogical(region);
        var physicalBounds = new SourceWindowBounds(
            _display.Left + (int)Math.Round(region.Left),
            _display.Top + (int)Math.Round(region.Top),
            _display.Left + (int)Math.Round(region.Right),
            _display.Top + (int)Math.Round(region.Bottom));
        return new WindowSelectionTarget(0, physicalBounds, logicalBounds, int.MaxValue);
    }

    private static bool NearlyEqual(SelectionRect first, SelectionRect second) =>
        Math.Abs(first.Left - second.Left) <= 2
        && Math.Abs(first.Top - second.Top) <= 2
        && Math.Abs(first.Right - second.Right) <= 2
        && Math.Abs(first.Bottom - second.Bottom) <= 2;

    private static bool HaveSameTargets(
        IReadOnlyList<WindowSelectionTarget> first,
        IReadOnlyList<WindowSelectionTarget> second)
    {
        if (first.Count != second.Count) return false;
        for (int index = 0; index < first.Count; index++)
        {
            if (first[index].Handle != second[index].Handle
                || !NearlyEqual(first[index].Bounds, second[index].Bounds))
                return false;
        }
        return true;
    }
}
