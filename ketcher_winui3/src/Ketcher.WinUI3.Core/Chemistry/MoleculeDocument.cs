using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Chemistry;

/// <summary>分子文档：原子、键、选择集和 SDF properties。</summary>
public sealed class MoleculeDocument
{
    private readonly Dictionary<int, Atom> _atoms = new();
    private readonly Dictionary<int, Bond> _bonds = new();
    private int _nextAtomId = 1;
    private int _nextBondId = 1;

    public string? Name { get; set; }
    public int DocumentVersion { get; set; }

    /// <summary>SDF property records，按原始顺序保留。</summary>
    public List<KeyValuePair<string, string>> Properties { get; } = new();

    public IReadOnlyCollection<Atom> Atoms => _atoms.Values;
    public IReadOnlyCollection<Bond> Bonds => _bonds.Values;

    public int AtomCount => _atoms.Count;
    public int BondCount => _bonds.Count;

    public Atom AddAtom(string element, Vector2 position)
    {
        var atom = new Atom(_nextAtomId++, element, position);
        _atoms[atom.Id] = atom;
        return atom;
    }

    public Bond AddBond(int startAtomId, int endAtomId, int order)
    {
        var bond = new Bond(_nextBondId++, startAtomId, endAtomId, order);
        _bonds[bond.Id] = bond;
        return bond;
    }

    public void RemoveAtom(int atomId)
    {
        // 移除与该原子相连的所有键
        var bondIdsToRemove = _bonds.Values
            .Where(b => b.StartAtomId == atomId || b.EndAtomId == atomId)
            .Select(b => b.Id)
            .ToList();
        foreach (var bondId in bondIdsToRemove)
            _bonds.Remove(bondId);

        _atoms.Remove(atomId);
    }

    public void RemoveBond(int bondId) => _bonds.Remove(bondId);

    public Atom? GetAtom(int id) => _atoms.GetValueOrDefault(id);
    public Bond? GetBond(int id) => _bonds.GetValueOrDefault(id);

    /// <summary>获取与指定原子相连的所有键。</summary>
    public IEnumerable<Bond> GetBondsForAtom(int atomId) =>
        _bonds.Values.Where(b => b.StartAtomId == atomId || b.EndAtomId == atomId);

    /// <summary>内部方法：直接添加原子（解析器用），不递增 ID 计数器。</summary>
    internal void AddAtomInternal(Atom atom)
    {
        _atoms[atom.Id] = atom;
        if (atom.Id >= _nextAtomId)
            _nextAtomId = atom.Id + 1;
    }

    /// <summary>内部方法：直接添加键（解析器用），不递增 ID 计数器。</summary>
    internal void AddBondInternal(Bond bond)
    {
        _bonds[bond.Id] = bond;
        if (bond.Id >= _nextBondId)
            _nextBondId = bond.Id + 1;
    }

    /// <summary>创建不可变快照，供渲染线程读取。</summary>
    public DocumentSnapshot CreateSnapshot() => new(
        Name,
        DocumentVersion,
        _atoms.Values.Select(a => new AtomSnapshot(a.Id, a.Element, a.Position, a.Charge, a.Isotope, a.Radical, a.ExplicitHCount, a.MappingNumber)).ToList(),
        _bonds.Values.Select(b => new BondSnapshot(b.Id, b.StartAtomId, b.EndAtomId, b.Order, b.IsAromatic, b.Stereo)).ToList(),
        Properties.ToList());

    public void Clear()
    {
        _atoms.Clear();
        _bonds.Clear();
        Properties.Clear();
        _nextAtomId = 1;
        _nextBondId = 1;
        Name = null;
    }
}

/// <summary>不可变原子快照。</summary>
public readonly record struct AtomSnapshot(
    int Id, string Element, Vector2 Position,
    int Charge, int Isotope, int Radical, int ExplicitHCount, int MappingNumber);

/// <summary>不可变键快照。</summary>
public readonly record struct BondSnapshot(
    int Id, int StartAtomId, int EndAtomId,
    int Order, bool IsAromatic, StereoDirection Stereo);

/// <summary>不可变文档快照，供渲染线程安全读取。</summary>
public sealed class DocumentSnapshot
{
    public string? Name { get; }
    public int DocumentVersion { get; }
    public IReadOnlyList<AtomSnapshot> Atoms { get; }
    public IReadOnlyList<BondSnapshot> Bonds { get; }
    public IReadOnlyList<KeyValuePair<string, string>> Properties { get; }

    public DocumentSnapshot(
        string? name, int documentVersion,
        IReadOnlyList<AtomSnapshot> atoms, IReadOnlyList<BondSnapshot> bonds,
        IReadOnlyList<KeyValuePair<string, string>> properties)
    {
        Name = name;
        DocumentVersion = documentVersion;
        Atoms = atoms;
        Bonds = bonds;
        Properties = properties;
    }
}
