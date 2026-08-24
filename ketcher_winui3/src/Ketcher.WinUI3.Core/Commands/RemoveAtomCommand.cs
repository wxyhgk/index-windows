using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Commands;

public sealed class RemoveAtomCommand : IEditorCommand
{
    private readonly int _atomId;
    private string _element;
    private Vector2 _position;
    private int _charge;
    private int _isotope;
    private int _radical;
    private int _explicitH;
    private int _mapping;
    private List<(int bondId, int startId, int endId, int order, bool aromatic, StereoDirection stereo)> _removedBonds = new();

    public RemoveAtomCommand(Atom atom)
    {
        _atomId = atom.Id;
        _element = atom.Element;
        _position = atom.Position;
        _charge = atom.Charge;
        _isotope = atom.Isotope;
        _radical = atom.Radical;
        _explicitH = atom.ExplicitHCount;
        _mapping = atom.MappingNumber;
    }

    public void Execute(MoleculeDocument doc)
    {
        _removedBonds = doc.GetBondsForAtom(_atomId)
            .Select(b => (b.Id, b.StartAtomId, b.EndAtomId, b.Order, b.IsAromatic, b.Stereo))
            .ToList();
        doc.RemoveAtom(_atomId);
    }

    public void Undo(MoleculeDocument doc)
    {
        foreach (var (bondId, startId, endId, order, aromatic, stereo) in _removedBonds)
        {
            var bond = doc.AddBond(startId, endId, order);
            bond.IsAromatic = aromatic;
            bond.Stereo = stereo;
        }
        var atom = doc.AddAtom(_element, _position);
        atom.Charge = _charge;
        atom.Isotope = _isotope;
        atom.Radical = _radical;
        atom.ExplicitHCount = _explicitH;
        atom.MappingNumber = _mapping;
    }
}
