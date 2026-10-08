using Index.Pin;
using Index.Platform.Windowing;
using Index.Toolbar;
using Index.UI.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Pin;

/// <summary>
/// Independent action palette for a pin. Keeping it out of the image window lets DWM move a
/// single, static image surface and prevents toolbar layout from affecting image dimensions.
/// </summary>
internal sealed class PinToolbarWindow : Window
{
    private readonly ToolbarRegistry _registry;
    private readonly ToolbarContext _context;
    private readonly ToolbarView _toolbar;
    private readonly Grid _root;
    private double _physicalWidth;
    private double _physicalHeight;
    private bool _initialized;
    private bool _isVisible;
    private bool _closed;

    public event Action<bool>? HoverChanged;

    public PinToolbarWindow(ToolbarRegistry registry, ToolbarContext context)
    {
        _registry = registry;
        _context = context;
        AppWindow.Title = "Index · 钉图工具栏";

        _toolbar = new ToolbarView
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _root = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _toolbar }
        };
        _root.PointerEntered += OnPointerEntered;
        _root.PointerExited += OnPointerExited;
        Content = _root;
        Closed += OnClosed;
    }

    public void Initialize(Window owner, PinRect imageFrame)
        => Initialize(WinRT.Interop.WindowNative.GetWindowHandle(owner), imageFrame);

    public void Initialize(nint ownerHwnd, PinRect imageFrame)
    {
        if (_closed) return;
        ConfigurePresenter();
        OwnedWindowRelationship.Attach(ownerHwnd, this);
        RefreshAndPlace(imageFrame);
        if (!_initialized)
        {
            // Materialize WinUI's control templates outside the native pin WndProc. The first
            // pointer-driven Show used to apply the Button templates reentrantly and surfaced as
            // a stowed Microsoft.UI.Xaml 0x802B000A process crash.
            PinWindowHost.MoveAndResize(
                this,
                new PinRect(-32000, -32000, _physicalWidth, _physicalHeight));
            AppWindow.Show(false);
            AppWindow.Hide();
            Place(imageFrame, _physicalWidth, _physicalHeight);
        }
        _initialized = true;
        _isVisible = false;
    }

    public void ShowAdjacent(PinRect imageFrame)
    {
        if (_closed || !_initialized) return;
        RefreshAndPlace(imageFrame);
        if (!_isVisible)
        {
            // The toolbar never activates: the pin retains keyboard focus and text selection.
            AppWindow.Show(false);
            _isVisible = true;
        }
    }

    public void MoveAdjacent(PinRect imageFrame)
    {
        if (_closed || _physicalWidth <= 0 || _physicalHeight <= 0)
            return;
        Place(imageFrame, _physicalWidth, _physicalHeight);
    }

    public void Hide()
    {
        if (!_closed && _isVisible)
        {
            AppWindow.Hide();
            _isVisible = false;
        }
    }

    public void Refresh(PinRect imageFrame)
    {
        if (!_closed)
            RefreshAndPlace(imageFrame);
    }

    public void Dismiss()
    {
        if (!_closed)
            Close();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
        => HoverChanged?.Invoke(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
        => HoverChanged?.Invoke(false);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _isVisible = false;
        _root.PointerEntered -= OnPointerEntered;
        _root.PointerExited -= OnPointerExited;
        Closed -= OnClosed;
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
            return;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
    }

    private void RefreshAndPlace(PinRect imageFrame)
    {
        var layout = _toolbar.Update(
            _registry,
            _context,
            new ToolbarRect(0, 0, 4096, 4096),
            new ToolbarRect(2048, 2048, 1, 1));
        double scale = PinWindowHost.DpiScale(this);
        double width = Math.Ceiling(layout.Width * scale);
        double height = Math.Ceiling(layout.Height * scale);
        _physicalWidth = width;
        _physicalHeight = height;
        Place(imageFrame, width, height);
    }

    private void Place(PinRect imageFrame, double width, double height)
    {
        var workArea = PinWindowHost.WorkAreaAt(imageFrame.MidX, imageFrame.MidY);
        var frame = PinToolbarGeometry.Place(imageFrame, width, height, workArea);
        PinWindowHost.MoveAndResize(this, frame);
    }
}
