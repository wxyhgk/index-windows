namespace Index.Capture;

/// <summary>
/// Pixel payload produced for both persistence and capture actions.
/// The artifact owns an immutable copy while <see cref="BasePng"/> remains the unannotated original.
/// </summary>
public sealed record PreparedCaptureImage(byte[] BasePng, CaptureArtifact Artifact);

/// <summary>
/// Converts frozen-display or direct-surface PNG data into the selected capture payload.
/// The contract deliberately knows nothing about windows, overlays, actions, or persistence.
/// </summary>
public interface ICaptureImagePreparer
{
    PreparedCaptureImage PrepareFrozen(
        CaptureSelection selection,
        ReadOnlyMemory<byte> frozenPng);

    PreparedCaptureImage? TryPrepareDirect(
        CaptureSelection selection,
        byte[] directPng);
}
