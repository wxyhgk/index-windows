using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI;

internal sealed class MainContentHost : Grid
{
    private readonly Grid _destinationLayer = new();
    private readonly Grid _overlayLayer = new();
    private FrameworkElement? _body;
    private FrameworkElement? _overlay;

    public MainContentHost()
    {
        Children.Add(_destinationLayer);
        Canvas.SetZIndex(_overlayLayer, 100);
        _overlayLayer.IsHitTestVisible = false;
        Children.Add(_overlayLayer);
    }

    public void ShowLibrary(FrameworkElement header)
    {
        _destinationLayer.Children.Clear();
        _destinationLayer.RowDefinitions.Clear();
        _destinationLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _destinationLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _body = null;
        Grid.SetRow(header, 0);
        _destinationLayer.Children.Add(header);
    }

    public void ShowLibraryBody(FrameworkElement body)
    {
        if (_body is not null)
            _destinationLayer.Children.Remove(_body);

        _body = body;
        Grid.SetRow(body, 1);
        _destinationLayer.Children.Add(body);
    }

    public void ShowPage(FrameworkElement page)
    {
        _destinationLayer.Children.Clear();
        _destinationLayer.RowDefinitions.Clear();
        _destinationLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _body = page;
        Grid.SetRow(page, 0);
        _destinationLayer.Children.Add(page);
    }

    public bool IsOverlayVisible(FrameworkElement overlay) =>
        ReferenceEquals(_overlay, overlay) && _overlayLayer.Children.Contains(overlay);

    public void ShowOverlay(FrameworkElement overlay)
    {
        if (IsOverlayVisible(overlay))
            return;

        if (_overlay is not null)
            _overlayLayer.Children.Remove(_overlay);

        _overlay = overlay;
        _overlayLayer.IsHitTestVisible = true;
        _overlayLayer.Children.Add(overlay);
    }

    public bool HideOverlay(FrameworkElement overlay)
    {
        if (!IsOverlayVisible(overlay))
            return false;

        _overlayLayer.Children.Remove(overlay);
        _overlay = null;
        _overlayLayer.IsHitTestVisible = false;
        return true;
    }
}
