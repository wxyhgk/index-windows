namespace Index.Capture;

/// <summary>
/// Read-only display topology required to bind a selection decision back to its frozen frame.
/// Capture adapters implement this contract without coupling the binding logic to one snapshot type.
/// </summary>
public interface ICaptureDisplayIdentitySource
{
    string DisplayId { get; }
    int Left { get; }
    int Top { get; }
    int Width { get; }
    int Height { get; }
    double DpiScale { get; }
}

/// <summary>
/// Pure binding logic between a captured selection identity and a frozen display batch.
/// </summary>
public static class CaptureDecisionBinding
{
    private const double ScaleTolerance = 0.000001;

    public static CaptureDisplayIdentity IdentityOf(ICaptureDisplayIdentitySource snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new CaptureDisplayIdentity(
            snapshot.DisplayId,
            snapshot.Left,
            snapshot.Top,
            snapshot.Width,
            snapshot.Height,
            snapshot.DpiScale);
    }

    public static TSnapshot ResolveSnapshot<TSnapshot>(
        CaptureDecision decision,
        IReadOnlyList<TSnapshot> frozenSnapshots)
        where TSnapshot : class, ICaptureDisplayIdentitySource
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(frozenSnapshots);

        var identity = decision.Selection.Display;
        if (string.IsNullOrWhiteSpace(identity.DisplayId))
            throw new InvalidOperationException("Capture selection has no display identity.");

        var sameId = frozenSnapshots
            .Where(snapshot => string.Equals(
                snapshot.DisplayId,
                identity.DisplayId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (sameId.Length == 0)
            throw new InvalidOperationException(
                $"Capture display '{identity.DisplayId}' is not part of the frozen batch.");
        if (sameId.Length > 1)
            throw new InvalidOperationException(
                $"Frozen batch contains duplicate display ID '{identity.DisplayId}'.");

        var snapshot = sameId[0];
        bool topologyMatches = snapshot.Left == identity.Left
            && snapshot.Top == identity.Top
            && snapshot.Width == identity.Width
            && snapshot.Height == identity.Height
            && Math.Abs(snapshot.DpiScale - identity.DpiScale) <= ScaleTolerance;
        if (!topologyMatches)
            throw new InvalidOperationException(
                $"Capture display '{identity.DisplayId}' topology changed after freezing.");

        return snapshot;
    }
}
