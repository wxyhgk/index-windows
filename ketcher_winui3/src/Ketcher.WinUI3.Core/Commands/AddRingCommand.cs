using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Commands;

/// <summary>添加环模板命令：一次创建环的所有原子和键，可整体撤销。</summary>
public sealed class AddRingCommand : IEditorCommand
{
    private readonly RingTemplate _template;
    private readonly Vector2 _center;
    private readonly List<int> _atomIds = new();
    private readonly List<int> _bondIds = new();

    public AddRingCommand(RingTemplate template, Vector2 center)
    {
        _template = template;
        _center = center;
    }

    public void Execute(MoleculeDocument doc)
    {
        _atomIds.Clear();
        _bondIds.Clear();

        foreach (var (pos, element) in _template.Atoms)
        {
            var atom = doc.AddAtom(element, pos + _center);
            _atomIds.Add(atom.Id);
        }

        foreach (var (start, end, order, aromatic) in _template.Bonds)
        {
            int startId = _atomIds[start - 1];
            int endId = _atomIds[end - 1];
            var bond = doc.AddBond(startId, endId, order);
            bond.IsAromatic = aromatic;
            _bondIds.Add(bond.Id);
        }
    }

    public void Undo(MoleculeDocument doc)
    {
        foreach (var bondId in _bondIds)
            doc.RemoveBond(bondId);
        foreach (var atomId in _atomIds)
            doc.RemoveAtom(atomId);
        _atomIds.Clear();
        _bondIds.Clear();
    }
}
