using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI;

internal sealed class MainContentHost : Grid
{
    private FrameworkElement? _body;
    private FrameworkElement? _overlay;

    public void ShowLibrary(FrameworkElement header)
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _body = null;
        _overlay = null;
        Grid.SetRow(header, 0);
        Children.Add(header);
    }

    public void ShowLibraryBody(FrameworkElement body)
    {
        if (_body is not null)
            Children.Remove(_body);

        _body = body;
        Grid.SetRow(body, 1);
        Children.Add(body);
    }

    public void ShowPage(FrameworkElement page)
    {
        Children.Clear();
        RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _body = page;
        _overlay = null;
        Grid.SetRow(page, 0);
        Children.Add(page);
    }

    public bool IsOverlayVisible(FrameworkElement overlay) =>
        ReferenceEquals(_overlay, overlay) && Children.Contains(overlay);

    public void ShowOverlay(FrameworkElement overlay)
    {
        if (IsOverlayVisible(overlay))
            return;

        if (_overlay is not null)
            Children.Remove(_overlay);

        _overlay = overlay;
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, Math.Max(1, RowDefinitions.Count));
        Canvas.SetZIndex(overlay, 100);
        Children.Add(overlay);
    }

    public bool HideOverlay(FrameworkElement overlay)
    {
        if (!IsOverlayVisible(overlay))
            return false;

        Children.Remove(overlay);
        _overlay = null;
        return true;
    }
}
