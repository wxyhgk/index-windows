namespace Index.Pin;

/// <summary>
/// Platform-independent zoom and opacity state for a pin session.
/// </summary>
public sealed class PinInteractionState
{
    private readonly int _naturalWidth;
    private readonly int _naturalHeight;

    public PinInteractionState(int naturalWidth, int naturalHeight)
    {
        if (naturalWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(naturalWidth));
        if (naturalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(naturalHeight));

        _naturalWidth = naturalWidth;
        _naturalHeight = naturalHeight;
    }

    public double Zoom { get; private set; } = 1;
    public double Opacity { get; private set; } = 1;
    public byte OpacityByte => checked((byte)Math.Round(Opacity * byte.MaxValue));

    public void Initialize(PinRect imageFrame)
    {
        Zoom = Math.Clamp(
            imageFrame.Width / _naturalWidth,
            PinWindowGeometry.MinimumZoom,
            PinWindowGeometry.MaximumZoom);
    }

    public double SteppedZoom(int wheelDelta) => wheelDelta switch
    {
        > 0 => Zoom * PinWindowGeometry.WheelStep,
        < 0 => Zoom / PinWindowGeometry.WheelStep,
        _ => Zoom
    };

    public PinRect ApplyZoom(PinRect currentImageFrame, double requestedZoom, PinRect workArea)
    {
        Zoom = Math.Clamp(
            requestedZoom,
            PinWindowGeometry.MinimumZoom,
            PinWindowGeometry.MaximumZoom);
        return PinWindowGeometry.ZoomAroundCenter(
            currentImageFrame,
            _naturalWidth,
            _naturalHeight,
            Zoom,
            workArea);
    }

    public void StepOpacity(int wheelDelta)
    {
        if (wheelDelta == 0)
            return;
        Opacity = Math.Clamp(Opacity + (wheelDelta > 0 ? 0.1 : -0.1), 0.2, 1);
    }
}
