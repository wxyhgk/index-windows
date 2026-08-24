namespace Ketcher.WinUI3.Core.Chemistry;

/// <summary>键的立体方向。</summary>
public enum StereoDirection
{
    None = 0,
    Up = 1,
    Down = 6,
    Either = 7,
    Neither = 8
}

/// <summary>分子中的键。使用稳定 ID 标识。</summary>
public sealed class Bond
{
    public int Id { get; }
    public int StartAtomId { get; }
    public int EndAtomId { get; }
    public int Order { get; set; }
    public bool IsAromatic { get; set; }
    public StereoDirection Stereo { get; set; }

    public Bond(int id, int startAtomId, int endAtomId, int order)
    {
        Id = id;
        StartAtomId = startAtomId;
        EndAtomId = endAtomId;
        Order = order;
    }

    /// <summary>从 MOL 键级字段解析。4 = 芳香键。</summary>
    public static (int order, bool aromatic) ParseOrder(int molOrder) => molOrder switch
    {
        4 => (0, true),
        _ => (molOrder, false)
    };

    /// <summary>将键级转换为 MOL 字段值。芳香键 = 4。</summary>
    public int ToMolOrder() => IsAromatic ? 4 : Order;

    public override string ToString() => $"Bond({Id}, {StartAtomId}-{EndAtomId}, order={Order}, aromatic={IsAromatic})";
}
