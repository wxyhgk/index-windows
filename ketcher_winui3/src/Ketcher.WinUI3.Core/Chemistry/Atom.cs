using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Chemistry;

/// <summary>分子中的原子。使用稳定 ID 标识，不依赖数组下标。</summary>
public sealed class Atom
{
    public int Id { get; }
    public string Element { get; set; }
    public Vector2 Position { get; set; }
    public int Charge { get; set; }
    public int Isotope { get; set; }
    public int Radical { get; set; }
    public int ExplicitHCount { get; set; }
    public int MappingNumber { get; set; }

    public Atom(int id, string element, Vector2 position)
    {
        Id = id;
        Element = element;
        Position = position;
    }

    public override string ToString() => $"Atom({Id}, {Element}, {Position})";
}
