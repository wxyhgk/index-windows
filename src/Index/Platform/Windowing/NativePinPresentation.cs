using Index.Pin;
using Index.Ocr;
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
    private PinPixelBuffer? _resizedImage;
    private PinPixelBuffer? _currentPixels;
    private IReadOnlyList<OcrPixelRect> _selectedTextBounds = [];
    private int _selectionCoordinateWidth;
    private int _selectionCoordinateHeight;
    private byte _opacity = byte.MaxValue;
    private bool _disposed;

    public NativePinPresentation(PinPixelBuffer source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public event Action<PinRect>? FrameChanged;
    public event Action<int, bool>? WheelChanged;
    public event Action? CloseRequested;
    public event Action<double, double>? TextPointerPressed;
    public event Action<double, double>? TextPointerMoved;
    public event Action<double, double>? TextPointerReleased;
    public event Action? TextPointerCanceled;
    public event Action<PinTextCommand>? TextCommandRequested;
    public event Action<bool>? HoverChanged;

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
            _opacity = opacity;
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
        _opacity = opacity;
        _currentPixels = PreparePixels(imageFrame);
        surface.Update(
            PinHighlight.OuterFrame(imageFrame),
            _currentPixels.Pixels,
            _currentPixels.Width,
            _currentPixels.Height,
            opacity);
    }

    public void EnableTextInteraction(Func<double, double, bool> hitTest)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        ActiveSurface().TextHitTest = hitTest;
    }

    public void UpdateTextSelection(
        IReadOnlyList<OcrPixelRect> selectedBounds,
        int coordinateWidth,
        int coordinateHeight)
    {
        ArgumentNullException.ThrowIfNull(selectedBounds);
        if (coordinateWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(coordinateWidth));
        if (coordinateHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(coordinateHeight));

        _selectedTextBounds = selectedBounds.ToArray();
        _selectionCoordinateWidth = coordinateWidth;
        _selectionCoordinateHeight = coordinateHeight;
        _currentPixels = null;
        var imageFrame = PinHighlight.ImageFrame(ActiveSurface().CurrentFrame);
        Update(imageFrame, _opacity);
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
        if (_resizedImage is not { } resized
            || resized.Width != width
            || resized.Height != height)
        {
            resized = PinPixelRenderer.Resize(_source, width, height);
            _resizedImage = resized;
        }
        return PinPixelRenderer.CreateHighlighted(
            resized,
            width,
            height,
            _selectedTextBounds,
            _selectionCoordinateWidth,
            _selectionCoordinateHeight);
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
        surface.TextPointerPressed += OnTextPointerPressed;
        surface.TextPointerMoved += OnTextPointerMoved;
        surface.TextPointerReleased += OnTextPointerReleased;
        surface.TextPointerCanceled += OnTextPointerCanceled;
        surface.TextCommandRequested += OnTextCommandRequested;
        surface.HoverChanged += OnHoverChanged;
    }

    private void ReleaseSurface()
    {
        var surface = _surface;
        _surface = null;
        _resizedImage = null;
        _currentPixels = null;
        if (surface is null) return;

        surface.FrameChanged -= OnFrameChanged;
        surface.WheelChanged -= OnWheelChanged;
        surface.CloseRequested -= OnCloseRequested;
        surface.TextPointerPressed -= OnTextPointerPressed;
        surface.TextPointerMoved -= OnTextPointerMoved;
        surface.TextPointerReleased -= OnTextPointerReleased;
        surface.TextPointerCanceled -= OnTextPointerCanceled;
        surface.TextCommandRequested -= OnTextCommandRequested;
        surface.HoverChanged -= OnHoverChanged;
        surface.TextHitTest = null;
        surface.Dispose();
    }

    private void OnFrameChanged(PinRect frame) => FrameChanged?.Invoke(frame);

    private void OnWheelChanged(int delta, bool controlDown) =>
        WheelChanged?.Invoke(delta, controlDown);

    private void OnCloseRequested() => CloseRequested?.Invoke();

    private void OnTextPointerPressed(double x, double y) =>
        TextPointerPressed?.Invoke(x, y);

    private void OnTextPointerMoved(double x, double y) =>
        TextPointerMoved?.Invoke(x, y);

    private void OnTextPointerReleased(double x, double y) =>
        TextPointerReleased?.Invoke(x, y);

    private void OnTextPointerCanceled() => TextPointerCanceled?.Invoke();

    private void OnTextCommandRequested(PinTextCommand command) =>
        TextCommandRequested?.Invoke(command);

    private void OnHoverChanged(bool hovering) => HoverChanged?.Invoke(hovering);
}

internal enum PinTextCommand
{
    Copy,
    SelectAll,
    Escape
}
