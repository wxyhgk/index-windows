using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Gallery;

/// <summary>图库窗口和卡片共享的动态画刷；修改颜色对象即可原位刷新整棵视觉树。</summary>
internal sealed class GalleryTheme
{
    public SolidColorBrush WindowBackground { get; } = Brush(0x0E, 0x10, 0x14);
    public SolidColorBrush Panel { get; } = Brush(0x18, 0x1B, 0x21);
    public SolidColorBrush PanelBorder { get; } = Brush(0x32, 0x37, 0x42);
    public SolidColorBrush Card { get; } = Brush(0x21, 0x25, 0x2D);
    public SolidColorBrush CardBorder { get; } = Brush(0x37, 0x3D, 0x49);
    public SolidColorBrush ThumbnailBackground { get; } = Brush(0x0B, 0x0D, 0x10);
    public SolidColorBrush Text { get; } = Brush(0xFF, 0xFF, 0xFF);
    public SolidColorBrush Muted { get; } = Brush(0x9B, 0xA3, 0xB4);
    public SolidColorBrush Selected { get; } = Brush(0x2B, 0x45, 0x70);
    public SolidColorBrush Accent { get; } = Brush(0x55, 0x3F, 0xFF);
    public SolidColorBrush ScreenshotHeader { get; } = Brush(0x45, 0x63, 0xC7);
    public SolidColorBrush RecordingHeader { get; } = Brush(0xC5, 0x67, 0x32);
    public SolidColorBrush HoverBorder { get; } = Brush(0x73, 0x83, 0xA3);

    public void Apply(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        Set(WindowBackground, dark ? (0x0E, 0x10, 0x14) : (0xF3, 0xF5, 0xF8));
        Set(Panel, dark ? (0x18, 0x1B, 0x21) : (0xFF, 0xFF, 0xFF));
        Set(PanelBorder, dark ? (0x32, 0x37, 0x42) : (0xD9, 0xDE, 0xE7));
        Set(Card, dark ? (0x21, 0x25, 0x2D) : (0xF8, 0xF9, 0xFB));
        Set(CardBorder, dark ? (0x37, 0x3D, 0x49) : (0xD8, 0xDD, 0xE6));
        Set(ThumbnailBackground, dark ? (0x0B, 0x0D, 0x10) : (0xEA, 0xED, 0xF2));
        Set(Text, dark ? (0xFF, 0xFF, 0xFF) : (0x18, 0x1B, 0x22));
        Set(Muted, dark ? (0x9B, 0xA3, 0xB4) : (0x62, 0x6B, 0x7A));
        Set(Selected, dark ? (0x2B, 0x45, 0x70) : (0xD9, 0xE7, 0xFA));
        Set(HoverBorder, dark ? (0x73, 0x83, 0xA3) : (0x8A, 0x98, 0xB2));
    }

    private static void Set(SolidColorBrush brush, (int R, int G, int B) color)
        => brush.Color = Windows.UI.Color.FromArgb(
            0xFF,
            checked((byte)color.R),
            checked((byte)color.G),
            checked((byte)color.B));

    private static SolidColorBrush Brush(byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(0xFF, r, g, b));
}
