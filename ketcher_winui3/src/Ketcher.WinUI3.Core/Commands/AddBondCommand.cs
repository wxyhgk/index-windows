using Ketcher.WinUI3.Core.Chemistry;

namespace Ketcher.WinUI3.Core.Commands;

public sealed class AddBondCommand : IEditorCommand
{
    private readonly int _startAtomId;
    private readonly int _endAtomId;
    private readonly int _order;
    private int _bondId;

    public AddBondCommand(int startAtomId, int endAtomId, int order)
    {
        _startAtomId = startAtomId;
        _endAtomId = endAtomId;
        _order = order;
    }

    public void Execute(MoleculeDocument doc)
    {
        var bond = doc.AddBond(_startAtomId, _endAtomId, _order);
        _bondId = bond.Id;
    }

    public void Undo(MoleculeDocument doc)
    {
        doc.RemoveBond(_bondId);
    }
}
