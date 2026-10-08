using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Index.UI.Toolbar;

internal static class ToolbarIconCatalog
{
    private static readonly FontFamily FluentIcons = new("Segoe Fluent Icons");

    public static FontIcon Create(
        string id,
        string fallback,
        Brush foreground,
        double fontSize)
        => new()
        {
            Glyph = Glyph(id, fallback),
            FontFamily = FluentIcons,
            FontSize = fontSize,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static FrameworkElement CreateLiveText(Brush foreground)
    {
        var icon = new Grid
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.Children.Add(new Rectangle
        {
            Width = 15,
            Height = 15,
            RadiusX = 2,
            RadiusY = 2,
            Stroke = foreground,
            StrokeThickness = 1.35
        });
        icon.Children.Add(Line(foreground, 8, new Thickness(0, -5, 0, 0)));
        icon.Children.Add(Line(foreground, 8, new Thickness(0)));
        icon.Children.Add(Line(foreground, 5, new Thickness(-3, 5, 0, 0)));
        return icon;
    }

    public static FrameworkElement CreateCounter(Brush foreground)
        => new Grid
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

    public static string Glyph(string id, string fallback) => id switch
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
        "pin.copy-text" => "\uE8C8",
        "pin" => "\uE718",
        "complete" => "\uE73E",
        "save" => "\uE74E",
        "close" => "\uE711",
        "action.cancel" => "\uE711",
        _ when id.StartsWith("shape.rect", StringComparison.Ordinal) => "\uF0D8",
        _ when id.StartsWith("shape.ellipse", StringComparison.Ordinal) => "\uEA3A",
        _ => fallback
    };

    private static Rectangle Line(Brush foreground, double width, Thickness margin)
        => new()
        {
            Width = width,
            Height = 1.4,
            RadiusX = 0.7,
            RadiusY = 0.7,
            Fill = foreground,
            Margin = margin
        };
}
