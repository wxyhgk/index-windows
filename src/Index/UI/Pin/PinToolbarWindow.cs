using Index.Pin;
using Index.Platform.Windowing;
using Index.Toolbar;
using Index.UI.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private double _physicalWidth;
    private double _physicalHeight;
    private bool _closed;

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
        Content = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { _toolbar }
        };
        Closed += (_, _) => _closed = true;
    }

    public void Show(Window owner, PinRect imageFrame)
        => Show(WinRT.Interop.WindowNative.GetWindowHandle(owner), imageFrame);

    public void Show(nint ownerHwnd, PinRect imageFrame)
    {
        if (_closed) return;
        ConfigurePresenter();
        OwnedWindowRelationship.Attach(ownerHwnd, this);
        RefreshAndPlace(imageFrame);
        // Show only after the presenter, owner and physical bounds are final. Activating first
        // exposes one frame of the default overlapped window and also steals focus from the pin.
        AppWindow.Show(false);
    }

    public void ShowAdjacent(PinRect imageFrame)
    {
        if (_closed) return;
        RefreshAndPlace(imageFrame);
        AppWindow.Show(false);
    }

    public void MoveAdjacent(PinRect imageFrame)
    {
        if (_closed || _physicalWidth <= 0 || _physicalHeight <= 0)
            return;
        Place(imageFrame, _physicalWidth, _physicalHeight);
    }

    public void Hide()
    {
        if (!_closed)
            AppWindow.Hide();
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
