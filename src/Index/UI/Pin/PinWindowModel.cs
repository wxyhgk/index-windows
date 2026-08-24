using Index.Capture;
using Index.Render;

namespace Index.UI.Pin;

internal sealed record PinWindowModel(
    CaptureArtifact Artifact,
    byte[] RenderedPng,
    PinPixelBuffer Pixels,
    CaptureRegion? Region,
    string? SuggestedFileName);
