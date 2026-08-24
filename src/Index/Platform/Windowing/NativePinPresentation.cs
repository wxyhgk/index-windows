using Index.Pin;
using Index.Render;

namespace Index.Platform.Windowing;

/// <summary>
/// Owns the native pin surface, its event subscriptions, and the currently presented pixels.
/// Dependent owned windows must be closed before this presentation is disposed.
/// </summary>
internal sealed class NativePinPresentation : IDisposable
{
    private readonly PinPixelBuffer _source;
    private NativePinSurface? _surface;
    private PinPixelBuffer? _currentPixels;
    private bool _disposed;

    public NativePinPresentation(PinPixelBuffer source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public event Action<PinRect>? FrameChanged;
    public event Action<int, bool>? WheelChanged;
    public event Action? CloseRequested;

    public bool IsActive => _surface is not null;

    public nint Handle => ActiveSurface().Handle;

    public PinRect CurrentFrame => ActiveSurface().CurrentFrame;

    public void Show(PinRect imageFrame, byte opacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_surface is not null)
            throw new InvalidOperationException("The native pin presentation is already visible.");

        var surface = new NativePinSurface();
        _surface = surface;
        Bind(surface);
        try
        {
            _currentPixels = PreparePixels(imageFrame);
            surface.Show(
                PinHighlight.OuterFrame(imageFrame),
                _currentPixels.Pixels,
                _currentPixels.Width,
                _currentPixels.Height,
                opacity);
        }
        catch
        {
            ReleaseSurface();
            throw;
        }
    }

    public void Update(PinRect imageFrame, byte opacity)
    {
        var surface = ActiveSurface();
        _currentPixels = PreparePixels(imageFrame);
        surface.Update(
            PinHighlight.OuterFrame(imageFrame),
            _currentPixels.Pixels,
            _currentPixels.Width,
            _currentPixels.Height,
            opacity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseSurface();
        GC.SuppressFinalize(this);
    }

    private PinPixelBuffer PreparePixels(PinRect imageFrame)
    {
        int width = Math.Max(1, checked((int)Math.Round(imageFrame.Width)));
        int height = Math.Max(1, checked((int)Math.Round(imageFrame.Height)));
        int outerWidth = checked(width + PinHighlight.Thickness * 2);
        int outerHeight = checked(height + PinHighlight.Thickness * 2);
        if (_currentPixels is { } current
            && current.Width == outerWidth
            && current.Height == outerHeight)
        {
            return current;
        }
        return PinPixelRenderer.CreateHighlighted(_source, width, height);
    }

    private NativePinSurface ActiveSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _surface
            ?? throw new InvalidOperationException("The native pin presentation is not visible.");
    }

    private void Bind(NativePinSurface surface)
    {
        surface.FrameChanged += OnFrameChanged;
        surface.WheelChanged += OnWheelChanged;
        surface.CloseRequested += OnCloseRequested;
    }

    private void ReleaseSurface()
    {
        var surface = _surface;
        _surface = null;
        _currentPixels = null;
        if (surface is null) return;

        surface.FrameChanged -= OnFrameChanged;
        surface.WheelChanged -= OnWheelChanged;
        surface.CloseRequested -= OnCloseRequested;
        surface.Dispose();
    }

    private void OnFrameChanged(PinRect frame) => FrameChanged?.Invoke(frame);

    private void OnWheelChanged(int delta, bool controlDown) =>
        WheelChanged?.Invoke(delta, controlDown);

    private void OnCloseRequested() => CloseRequested?.Invoke();
}
