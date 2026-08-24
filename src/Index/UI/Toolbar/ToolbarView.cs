using Index.Annotation;
using Index.Toolbar;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Index.UI.Toolbar;

/// <summary>
/// WinUI 只负责把注册表与纯布局结果变成视图；不认识截图保存或标注实现。
/// </summary>
public sealed class ToolbarView : Canvas
{
    private static readonly FontFamily FluentIcons = new("Segoe Fluent Icons");
    private string _renderSignature = "";

    public ToolbarLayoutResult Update(
        ToolbarRegistry registry,
        ToolbarContext context,
        ToolbarRect viewport,
        ToolbarRect anchor)
    {
        var controls = registry.ControlsFor(context);
        var layout = ToolbarLayout.Arrange(controls, context, viewport, anchor);
        string signature = string.Join('|', controls.Select(control =>
            $"{control.Id}:{control.IsEnabled(context)}:{control.IsSelected(context)}:{control.IsBusy(context)}" +
            (control is ShapeAnnotationToolbarControl shape ? $":{shape.ActiveTool(context)}" : "")));
        signature = $"{ActualTheme}:{layout.IsBelowAnchor}:{signature}";

        if (_renderSignature != signature)
        {
            Render(layout, context);
            _renderSignature = signature;
        }

        Width = layout.Width;
        Height = layout.Height;
        return layout;
    }

    private void Render(ToolbarLayoutResult layout, ToolbarContext context)
    {
        Children.Clear();
        var palette = ToolbarPalette.For(ActualTheme);

        foreach (var row in layout.RowFrames)
        {
            var background = new Border
            {
                Width = row.Width,
                Height = row.Height,
                CornerRadius = new CornerRadius(8),
                Background = palette.Background,
                BorderBrush = palette.Border,
                BorderThickness = new Thickness(1)
            };
            SetLeft(background, row.X);
            SetTop(background, row.Y);
            Children.Add(background);
        }

        foreach (var slot in layout.Slots)
        {
            if (slot.Control is not { } control)
            {
                var separator = new Rectangle
                {
                    Width = 1,
                    Height = 16,
                    Fill = palette.Separator
                };
                SetLeft(separator, slot.Frame.X + (slot.Frame.Width - 1) / 2);
                SetTop(separator, slot.Frame.Y + 9);
                Children.Add(separator);
                continue;
            }

            var button = CreateButton(control, context, slot, palette);
            SetLeft(button, slot.Frame.X);
            SetTop(button, slot.Frame.Y);
            Children.Add(button);
        }
    }

    private static Button CreateButton(
        IToolbarControl control,
        ToolbarContext context,
        ToolbarSlot slot,
        ToolbarPalette palette)
    {
        bool selected = control.IsSelected(context);
        bool enabled = control.IsEnabled(context);
        Brush restingFill = selected ? palette.Selected : TransparentBrush;
        Brush hoverFill = selected ? palette.SelectedHover : palette.Hover;

        var visualSurface = new Border
        {
            Margin = new Thickness(2.5, 3.5, 2.5, 3.5),
            CornerRadius = new CornerRadius(5),
            Background = restingFill,
            BorderBrush = TransparentBrush,
            BorderThickness = new Thickness(1),
            Child = CreateControlVisual(control, context, palette.Foreground)
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
            Background = TransparentBrush,
            Foreground = palette.Foreground,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            IsEnabled = enabled,
            Opacity = enabled ? 1 : control.IsBusy(context) ? 0.72 : 0.32,
            Content = visualSurface
        };

        // The stock WinUI template otherwise adds a second hover/pressed plate.
        SetButtonResource(button, "ButtonBackground", TransparentBrush);
        SetButtonResource(button, "ButtonBackgroundPointerOver", TransparentBrush);
        SetButtonResource(button, "ButtonBackgroundPressed", TransparentBrush);
        SetButtonResource(button, "ButtonBackgroundDisabled", TransparentBrush);
        SetButtonResource(button, "ButtonBorderBrush", TransparentBrush);
        SetButtonResource(button, "ButtonBorderBrushPointerOver", TransparentBrush);
        SetButtonResource(button, "ButtonBorderBrushPressed", TransparentBrush);
        SetButtonResource(button, "ButtonBorderBrushDisabled", TransparentBrush);

        button.PointerEntered += (_, _) => visualSurface.Background = hoverFill;
        button.PointerExited += (_, _) => visualSurface.Background = restingFill;
        button.PointerPressed += (_, _) => visualSurface.Background = palette.Pressed;
        button.PointerReleased += (_, _) => visualSurface.Background = hoverFill;
        button.GotFocus += (_, _) =>
        {
            visualSurface.BorderBrush = palette.Focus;
            visualSurface.BorderThickness = new Thickness(1.5);
        };
        button.LostFocus += (_, _) =>
        {
            visualSurface.BorderBrush = TransparentBrush;
            visualSurface.BorderThickness = new Thickness(1);
        };

        ToolTipService.SetToolTip(button, control.Label);
        button.Click += (_, _) =>
        {
            if (control is MoreAnnotationToolsToolbarControl moreTools)
            {
                ShowMoreToolsMenu(button, moreTools, context);
                return;
            }

            if (control is ShapeAnnotationToolbarControl shape)
            {
                ShowShapeMenu(button, shape, context);
                return;
            }

            control.Activate(context);
        };
        return button;
    }

    private static void ShowMoreToolsMenu(
        Button anchor,
        MoreAnnotationToolsToolbarControl control,
        ToolbarContext context)
    {
        var menu = new MenuFlyout();
        foreach (var descriptor in control.Descriptors)
        {
            var tool = descriptor.Tool;
            var item = new MenuFlyoutItem
            {
                Text = tool.Title(),
                Icon = new FontIcon
                {
                    Glyph = FluentGlyph(AnnotationToolbarControlIds.Tool(tool), "\u2022"),
                    FontFamily = FluentIcons
                }
            };
            item.Click += (_, _) => control.SelectTool(tool, context);
            menu.Items.Add(item);
        }

        menu.ShowAt(anchor);
    }

    private static void ShowShapeMenu(
        Button anchor,
        ShapeAnnotationToolbarControl control,
        ToolbarContext context)
    {
        var menu = new MenuFlyout();
        foreach (var tool in ShapeAnnotationToolbarControl.Tools)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = tool.Title(),
                IsChecked = context.Annotation.Tool == tool,
                Icon = new FontIcon
                {
                    Glyph = FluentGlyph(AnnotationToolbarControlIds.Tool(tool), "\u25A1"),
                    FontFamily = FluentIcons
                }
            };
            item.Click += (_, _) => control.SelectTool(tool, context);
            menu.Items.Add(item);
        }

        menu.ShowAt(anchor);
    }

    private static FrameworkElement CreateControlVisual(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
    {
        if (control.IsBusy(context))
        {
            return new ProgressRing
            {
                Width = 14,
                Height = 14,
                IsActive = true,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (control is AnnotationColorToolbarControl colorControl)
        {
            var color = colorControl.Color;
            return new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = ColorBrush(
                    (byte)Math.Clamp(color.A * 255, 0, 255),
                    (byte)Math.Clamp(color.R * 255, 0, 255),
                    (byte)Math.Clamp(color.G * 255, 0, 255),
                    (byte)Math.Clamp(color.B * 255, 0, 255)),
                BorderBrush = foreground,
                BorderThickness = new Thickness(0.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (control is AnnotationStyleParameterToolbarControl parameter)
            return CreateParameterVisual(parameter, foreground);

        if (control is ShapeAnnotationToolbarControl shape)
        {
            var tool = shape.ActiveTool(context);
            return CreateFluentIcon(AnnotationToolbarControlIds.Tool(tool), shape.Glyph, foreground, 16);
        }

        if (control.Id == AnnotationToolbarControlIds.Tool(AnnotationTool.Counter))
            return CreateCounterIcon(foreground);

        return CreateFluentIcon(
            control.Id,
            control.Glyph,
            foreground,
            control.Group == ToolbarGroup.Style ? 14 : 16);
    }

    private static FrameworkElement CreateCounterIcon(Brush foreground)
    {
        return new Grid
        {
            Width = 17,
            Height = 17,
            Children =
            {
                new Ellipse
                {
                    Stroke = foreground,
                    StrokeThickness = 1.6
                },
                new TextBlock
                {
                    Text = "1",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -1, 0, 0)
                }
            }
        };
    }

    private static FontIcon CreateFluentIcon(
        string id,
        string fallback,
        Brush foreground,
        double fontSize)
        => new()
        {
            Glyph = FluentGlyph(id, fallback),
            FontFamily = FluentIcons,
            FontSize = fontSize,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static FrameworkElement CreateParameterVisual(
        AnnotationStyleParameterToolbarControl parameter,
        Brush foreground)
    {
        if (parameter.Descriptor.Axis == ToolStyleAxis.FontSize)
        {
            return new TextBlock
            {
                Text = "A",
                FontSize = 9 + parameter.Index * 2.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        double diameter = parameter.Descriptor.Axis == ToolStyleAxis.Width
            ? 3 + parameter.Index * 3
            : 10;
        double opacity = parameter.Descriptor.Axis is ToolStyleAxis.Opacity or ToolStyleAxis.Dim
            ? parameter.Value
            : 0.9;
        return new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = foreground,
            Opacity = opacity,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>Stable semantic IDs map to one icon family at the UI boundary.</summary>
    private static string FluentGlyph(string id, string fallback) => id switch
    {
        "tool.pointer" => "\uE7C9",
        "tool.rect" => "\uF0D8",
        "tool.ellipse" => "\uEA3A",
        "tool.shape" => "\uF0D8",
        "tool.arrow" => "\uE72A",
        "tool.line" => "\uE8A1",
        "tool.text" => "\uE8D2",
        "tool.highlight" => "\uE7E6",
        "tool.pixelate" => "\uE950",
        "tool.crop" => "\uE7A8",
        "tool.counter" => "\uE9D0",
        "tool.spotlight" => "\uE890",
        "tool.dimension" => "\uED5E",
        "tool.more" => "\uE712",
        "history.undo" => "\uE7A7",
        "history.redo" => "\uE7A6",
        "copy" => "\uE8C8",
        "pin" => "\uE718",
        "complete" => "\uE73E",
        "save" => "\uE73E",
        "close" => "\uE711",
        "action.cancel" => "\uE711",
        _ when id.StartsWith("shape.rect", StringComparison.Ordinal) => "\uF0D8",
        _ when id.StartsWith("shape.ellipse", StringComparison.Ordinal) => "\uEA3A",
        _ => fallback
    };

    private static void SetButtonResource(Button button, string key, Brush value)
        => button.Resources[key] = value;

    private static SolidColorBrush ColorBrush(byte a, byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(a, r, g, b));

    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    private sealed record ToolbarPalette(
        Brush Background,
        Brush Border,
        Brush Separator,
        Brush Foreground,
        Brush Hover,
        Brush Selected,
        Brush SelectedHover,
        Brush Pressed,
        Brush Focus)
    {
        public static ToolbarPalette For(ElementTheme theme)
        {
            if (theme == ElementTheme.Light)
            {
                return new ToolbarPalette(
                    ColorBrush(0xFA, 0xF8, 0xF8, 0xF8),
                    ColorBrush(0x24, 0x00, 0x00, 0x00),
                    ColorBrush(0x24, 0x00, 0x00, 0x00),
                    ColorBrush(0xE8, 0x20, 0x23, 0x28),
                    ColorBrush(0x10, 0x00, 0x00, 0x00),
                    ColorBrush(0x26, 0x2F, 0x7D, 0xFF),
                    ColorBrush(0x38, 0x2F, 0x7D, 0xFF),
                    ColorBrush(0x52, 0x2F, 0x7D, 0xFF),
                    ColorBrush(0xFF, 0x2F, 0x7D, 0xFF));
            }

            return new ToolbarPalette(
                ColorBrush(0xF5, 0x20, 0x22, 0x28),
                ColorBrush(0x24, 0xFF, 0xFF, 0xFF),
                ColorBrush(0x2E, 0xFF, 0xFF, 0xFF),
                ColorBrush(0xF2, 0xFF, 0xFF, 0xFF),
                ColorBrush(0x20, 0xFF, 0xFF, 0xFF),
                ColorBrush(0xD9, 0x2F, 0x7D, 0xFF),
                ColorBrush(0xF0, 0x3A, 0x84, 0xFF),
                ColorBrush(0xFF, 0x1F, 0x68, 0xE5),
                ColorBrush(0xFF, 0x70, 0xAC, 0xFF));
        }
    }
}
