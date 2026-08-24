using Index.Pin;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Index.Platform.Windowing;

/// <summary>
/// Owns the non-client caption region and pointer-drag fallback for a WinUI pin window.
/// Toolbar presentation stays outside the platform layer and is notified through callbacks.
/// </summary>
internal sealed class PinFallbackCaptionController : IDisposable
{
    private readonly Window _window;
    private readonly UIElement _dragSurface;
    private readonly Action _onFallbackMoveStarting;
    private readonly Action<PinRect> _onFrameChanging;
    private readonly Action<PinRect> _onMoveCompleted;
    private InputNonClientPointerSource? _nonClientInput;
    private bool _usesPointerFallback;
    private bool _started;
    private bool _disposed;

    public PinFallbackCaptionController(
        Window window,
        UIElement dragSurface,
        Action onFallbackMoveStarting,
        Action<PinRect> onFrameChanging,
        Action<PinRect> onMoveCompleted)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _dragSurface = dragSurface ?? throw new ArgumentNullException(nameof(dragSurface));
        _onFallbackMoveStarting = onFallbackMoveStarting
            ?? throw new ArgumentNullException(nameof(onFallbackMoveStarting));
        _onFrameChanging = onFrameChanging
            ?? throw new ArgumentNullException(nameof(onFrameChanging));
        _onMoveCompleted = onMoveCompleted
            ?? throw new ArgumentNullException(nameof(onMoveCompleted));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        _started = true;
        try
        {
            _nonClientInput = InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id);
            _nonClientInput.ExitedMoveSize += OnExitedMoveSize;
            _nonClientInput.WindowRectChanging += OnWindowRectChanging;
            UpdateCaptionRegion();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Native pin caption unavailable: {error}");
            DetachNonClientInput(clearCaption: true);
            _usesPointerFallback = true;
            _dragSurface.PointerPressed += OnPointerPressed;
        }
    }

    public void UpdateCaptionRegion()
    {
        if (_disposed || _nonClientInput is null)
            return;

        var size = _window.AppWindow.Size;
        _nonClientInput.SetRegionRects(
            NonClientRegionKind.Caption,
            [new Windows.Graphics.RectInt32(
                0,
                0,
                Math.Max(1, size.Width),
                Math.Max(1, size.Height))]);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_usesPointerFallback)
        {
            _dragSurface.PointerPressed -= OnPointerPressed;
            _usesPointerFallback = false;
        }
        DetachNonClientInput(clearCaption: true);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(_dragSurface);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        args.Handled = true;
        _onFallbackMoveStarting();
        PinWindowHost.BeginMove(_window);
        _onMoveCompleted(PinWindowHost.CurrentFrame(_window));
    }

    private void OnExitedMoveSize(
        InputNonClientPointerSource sender,
        ExitedMoveSizeEventArgs args) =>
        _onMoveCompleted(PinWindowHost.CurrentFrame(_window));

    private void OnWindowRectChanging(
        InputNonClientPointerSource sender,
        WindowRectChangingEventArgs args)
    {
        var rect = args.NewWindowRect;
        _onFrameChanging(new PinRect(rect.X, rect.Y, rect.Width, rect.Height));
    }

    private void DetachNonClientInput(bool clearCaption)
    {
        if (_nonClientInput is not { } nonClientInput)
            return;

        nonClientInput.ExitedMoveSize -= OnExitedMoveSize;
        nonClientInput.WindowRectChanging -= OnWindowRectChanging;
        if (clearCaption)
        {
            try
            {
                nonClientInput.ClearRegionRects(NonClientRegionKind.Caption);
            }
            catch (Exception error)
            {
                // The AppWindow can already be gone when WinUI raises Closed. Event handlers
                // are detached first, so a failed region cleanup cannot retain this controller.
                System.Diagnostics.Debug.WriteLine($"Pin caption cleanup skipped: {error}");
            }
        }
        _nonClientInput = null;
    }
}
