using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Commands;

public sealed class MoveAtomsCommand : IEditorCommand
{
    private readonly Dictionary<int, (Vector2 oldPos, Vector2 newPos)> _moves;

    public MoveAtomsCommand(IEnumerable<(int atomId, Vector2 oldPos, Vector2 newPos)> moves)
    {
        _moves = moves.ToDictionary(m => m.atomId, m => (m.oldPos, m.newPos));
    }

    public void Execute(MoleculeDocument doc)
    {
        foreach (var (atomId, (_, newPos)) in _moves)
            if (doc.GetAtom(atomId) is { } atom)
                atom.Position = newPos;
    }

    public void Undo(MoleculeDocument doc)
    {
        foreach (var (atomId, (oldPos, _)) in _moves)
            if (doc.GetAtom(atomId) is { } atom)
                atom.Position = oldPos;
    }
}
