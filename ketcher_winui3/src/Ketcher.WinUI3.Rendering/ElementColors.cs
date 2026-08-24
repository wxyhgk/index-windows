using SkiaSharp;

namespace Ketcher.WinUI3.Rendering;

/// <summary>元素颜色方案（Ketcher 配色，源自 ketcher-core ElementColor）。</summary>
public static class ElementColors
{
    private static readonly Dictionary<string, SKColor> _colors = new()
    {
        ["H"] = new SKColor(0x00, 0x00, 0x00),
        ["C"] = new SKColor(0x00, 0x00, 0x00),
        ["N"] = new SKColor(0x30, 0x4f, 0xf7),
        ["O"] = new SKColor(0xff, 0x0d, 0x0d),
        ["F"] = new SKColor(0x78, 0xbc, 0x42),
        ["P"] = new SKColor(0xff, 0x80, 0x00),
        ["S"] = new SKColor(0xc9, 0x9a, 0x19),
        ["Cl"] = new SKColor(0x1f, 0xd0, 0x1f),
        ["Br"] = new SKColor(0xa6, 0x29, 0x29),
        ["I"] = new SKColor(0x94, 0x00, 0x94),
        ["B"] = new SKColor(0xc1, 0x89, 0x89),
        ["Si"] = new SKColor(0xb2, 0x94, 0x78),
        ["Na"] = new SKColor(0xab, 0x5c, 0xf2),
        ["Mg"] = new SKColor(0x6f, 0xcd, 0x00),
        ["Al"] = new SKColor(0xe2, 0xc0, 0xc0),
        ["K"] = new SKColor(0x88, 0x4e, 0xbf),
        ["Ca"] = new SKColor(0x3d, 0xe1, 0x3d),
        ["Fe"] = new SKColor(0xe0, 0x66, 0x33),
        ["Cu"] = new SKColor(0xc7, 0x80, 0x33),
        ["Zn"] = new SKColor(0x7d, 0x80, 0xb0),
    };

    public static SKColor Get(string element) =>
        _colors.TryGetValue(element, out var color) ? color : new SKColor(0x80, 0x80, 0x80);
}
