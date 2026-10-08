using Index.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Index.UI.Toolbar;

internal sealed class ToolbarRenderer
{
    private readonly Canvas _host;
    private readonly ToolbarButtonFactory _buttons;

    public ToolbarRenderer(
        Canvas host,
        ToolbarControlPresenterRegistry presenters,
        ToolbarActivationRouter activationRouter)
    {
        _host = host;
        _buttons = new ToolbarButtonFactory(presenters, activationRouter);
    }

    public void Render(
        ToolbarLayoutResult layout,
        ToolbarContext context,
        ToolbarAppearance appearance)
    {
        _host.Children.Clear();

        foreach (var row in layout.RowFrames)
        {
            var background = new Border
            {
                Width = row.Width,
                Height = row.Height,
                CornerRadius = new CornerRadius(appearance.RowCornerRadius),
                Background = appearance.Background,
                BorderBrush = appearance.Border,
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(background, row.X);
            Canvas.SetTop(background, row.Y);
            _host.Children.Add(background);
        }

        foreach (var slot in layout.Slots)
        {
            FrameworkElement element = slot.Control is { } control
                ? _buttons.Create(control, context, slot, appearance)
                : CreateSeparator(slot, appearance);
            Canvas.SetLeft(element, slot.Frame.X);
            Canvas.SetTop(element, slot.Frame.Y);
            _host.Children.Add(element);
        }
    }

    private static Rectangle CreateSeparator(
        ToolbarSlot slot,
        ToolbarAppearance appearance)
        => new()
        {
            Width = 1,
            Height = 16,
            Fill = appearance.Separator,
            Margin = new Thickness((slot.Frame.Width - 1) / 2, 9, 0, 0)
        };
}
