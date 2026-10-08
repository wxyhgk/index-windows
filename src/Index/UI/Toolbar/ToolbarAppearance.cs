using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Toolbar;

public enum ToolbarVisualStyle
{
    Standard,
    PinFloating
}

internal sealed record ToolbarAppearance(
    double RowCornerRadius,
    Thickness ButtonMargin,
    double ButtonCornerRadius,
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
    public static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    public static ToolbarAppearance Resolve(
        ElementTheme theme,
        ToolbarVisualStyle visualStyle)
    {
        if (visualStyle == ToolbarVisualStyle.PinFloating)
        {
            return new ToolbarAppearance(
                17,
                new Thickness(2.5),
                11,
                Color(0xF2, 0x18, 0x1B, 0x21),
                Color(0x28, 0xFF, 0xFF, 0xFF),
                Color(0x22, 0xFF, 0xFF, 0xFF),
                Color(0xFA, 0xFF, 0xFF, 0xFF),
                Color(0x20, 0xFF, 0xFF, 0xFF),
                Color(0xD9, 0x2F, 0x7D, 0xFF),
                Color(0xF0, 0x48, 0x8E, 0xFF),
                Color(0xFF, 0x27, 0x6F, 0xE8),
                Color(0xFF, 0x78, 0xB2, 0xFF));
        }

        if (theme == ElementTheme.Light)
        {
            return new ToolbarAppearance(
                8,
                new Thickness(2.5, 3.5, 2.5, 3.5),
                5,
                Color(0xFA, 0xF8, 0xF8, 0xF8),
                Color(0x24, 0x00, 0x00, 0x00),
                Color(0x24, 0x00, 0x00, 0x00),
                Color(0xE8, 0x20, 0x23, 0x28),
                Color(0x10, 0x00, 0x00, 0x00),
                Color(0x26, 0x2F, 0x7D, 0xFF),
                Color(0x38, 0x2F, 0x7D, 0xFF),
                Color(0x52, 0x2F, 0x7D, 0xFF),
                Color(0xFF, 0x2F, 0x7D, 0xFF));
        }

        return new ToolbarAppearance(
            8,
            new Thickness(2.5, 3.5, 2.5, 3.5),
            5,
            Color(0xF5, 0x20, 0x22, 0x28),
            Color(0x24, 0xFF, 0xFF, 0xFF),
            Color(0x2E, 0xFF, 0xFF, 0xFF),
            Color(0xF2, 0xFF, 0xFF, 0xFF),
            Color(0x20, 0xFF, 0xFF, 0xFF),
            Color(0xD9, 0x2F, 0x7D, 0xFF),
            Color(0xF0, 0x3A, 0x84, 0xFF),
            Color(0xFF, 0x1F, 0x68, 0xE5),
            Color(0xFF, 0x70, 0xAC, 0xFF));
    }

    private static SolidColorBrush Color(byte a, byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(a, r, g, b));
}
