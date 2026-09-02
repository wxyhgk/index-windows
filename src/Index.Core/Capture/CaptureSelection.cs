using Index.Annotation;

namespace Index.Capture;

/// <summary>A completed selection expressed in frozen-image physical pixels.</summary>
public sealed record CaptureSelection
{
    /// <summary>
    /// Identity of the frozen display whose local pixel coordinates this selection uses.
    /// Bounds and DPI are included so a reused/stale display ID cannot bind to new topology.
    /// </summary>
    public required CaptureDisplayIdentity Display { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Layers<ImageSpace> Layers { get; init; }
}

/// <summary>The user's requested action and the immutable selection it should consume.</summary>
public sealed record CaptureDecision(
    string ActionId,
    CaptureSelection Selection,
    nint TargetWindowHandle = default);

/// <summary>
/// Immutable reference to one member of a frozen display batch. DisplayId is the stable key;
/// geometry and DPI make the reference topology-sensitive.
/// </summary>
public readonly record struct CaptureDisplayIdentity(
    string DisplayId,
    int Left,
    int Top,
    int Width,
    int Height,
    double DpiScale);
