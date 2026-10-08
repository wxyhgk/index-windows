using Index.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Toolbar;

internal sealed class ToolbarButtonFactory
{
    private readonly ToolbarControlPresenterRegistry _presenters;
    private readonly ToolbarActivationRouter _activationRouter;

    public ToolbarButtonFactory(
        ToolbarControlPresenterRegistry presenters,
        ToolbarActivationRouter activationRouter)
    {
        _presenters = presenters;
        _activationRouter = activationRouter;
    }

    public Button Create(
        IToolbarControl control,
        ToolbarContext context,
        ToolbarSlot slot,
        ToolbarAppearance appearance)
    {
        bool selected = control.IsSelected(context);
        bool enabled = control.IsEnabled(context);
        var restingFill = selected ? appearance.Selected : ToolbarAppearance.Transparent;
        var hoverFill = selected ? appearance.SelectedHover : appearance.Hover;
        var visualSurface = new Border
        {
            Margin = appearance.ButtonMargin,
            CornerRadius = new CornerRadius(appearance.ButtonCornerRadius),
            Background = restingFill,
            BorderBrush = ToolbarAppearance.Transparent,
            BorderThickness = new Thickness(1),
            Child = _presenters.Create(control, context, appearance.Foreground)
        };

        var button = new Button
        {
            Width = slot.Frame.Width,
            Height = slot.Frame.Height,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0),
            Background = ToolbarAppearance.Transparent,
            Foreground = appearance.Foreground,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsEnabled = enabled,
            Opacity = enabled ? 1 : control.IsBusy(context) ? 0.72 : 0.32,
            Content = visualSurface
        };

        RemoveStockButtonChrome(button);
        AttachInteractionVisuals(button, visualSurface, restingFill, hoverFill, appearance);
        ToolTipService.SetToolTip(button, control.Label);
        button.Click += (_, _) => _activationRouter.Activate(button, control, context);
        return button;
    }

    private static void RemoveStockButtonChrome(Button button)
    {
        SetResource(button, "ButtonBackground");
        SetResource(button, "ButtonBackgroundPointerOver");
        SetResource(button, "ButtonBackgroundPressed");
        SetResource(button, "ButtonBackgroundDisabled");
        SetResource(button, "ButtonBorderBrush");
        SetResource(button, "ButtonBorderBrushPointerOver");
        SetResource(button, "ButtonBorderBrushPressed");
        SetResource(button, "ButtonBorderBrushDisabled");
    }

    private static void AttachInteractionVisuals(
        Button button,
        Border visualSurface,
        Microsoft.UI.Xaml.Media.Brush restingFill,
        Microsoft.UI.Xaml.Media.Brush hoverFill,
        ToolbarAppearance appearance)
    {
        button.PointerEntered += (_, _) => visualSurface.Background = hoverFill;
        button.PointerExited += (_, _) => visualSurface.Background = restingFill;
        button.PointerPressed += (_, _) => visualSurface.Background = appearance.Pressed;
        button.PointerReleased += (_, _) => visualSurface.Background = hoverFill;
        button.GotFocus += (_, _) =>
        {
            visualSurface.BorderBrush = appearance.Focus;
            visualSurface.BorderThickness = new Thickness(1.5);
        };
        button.LostFocus += (_, _) =>
        {
            visualSurface.BorderBrush = ToolbarAppearance.Transparent;
            visualSurface.BorderThickness = new Thickness(1);
        };
    }

    private static void SetResource(Button button, string key)
        => button.Resources[key] = ToolbarAppearance.Transparent;
}
