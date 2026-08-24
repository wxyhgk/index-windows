using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Commands;

public sealed class AddAtomCommand : IEditorCommand
{
    private readonly string _element;
    private readonly Vector2 _position;
    private int _atomId;

    public int NewAtomId => _atomId;

    public AddAtomCommand(string element, Vector2 position)
    {
        _element = element;
        _position = position;
    }

    public void Execute(MoleculeDocument doc)
    {
        var atom = doc.AddAtom(_element, _position);
        _atomId = atom.Id;
    }

    public void Undo(MoleculeDocument doc)
    {
        doc.RemoveAtom(_atomId);
    }
}
