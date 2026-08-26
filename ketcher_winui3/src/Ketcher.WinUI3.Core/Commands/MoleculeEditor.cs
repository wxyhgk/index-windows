using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Commands;

/// <summary>分子编辑器：管理文档和命令栈（撤销/重做）。</summary>
public sealed class MoleculeEditor
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();

    public MoleculeDocument Document { get; } = new();
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler? DocumentChanged;

    public void Execute(IEditorCommand command)
    {
        command.Execute(Document);
        _undoStack.Push(command);
        _redoStack.Clear();
        NotifyChanged();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var cmd = _undoStack.Pop();
        cmd.Undo(Document);
        _redoStack.Push(cmd);
        NotifyChanged();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var cmd = _redoStack.Pop();
        cmd.Execute(Document);
        _undoStack.Push(cmd);
        NotifyChanged();
    }

    public Atom AddAtom(string element, Vector2 position)
    {
        var cmd = new AddAtomCommand(element, position);
        Execute(cmd);
        return Document.GetAtom(cmd.NewAtomId)!;
    }

    public void RemoveAtom(int atomId)
    {
        if (Document.GetAtom(atomId) is { } atom)
            Execute(new RemoveAtomCommand(atom));
    }

    public void AddBond(int startAtomId, int endAtomId, int order = 1)
    {
        // 检查是否已有键
        var existing = Document.GetBondsForAtom(startAtomId)
            .FirstOrDefault(b => b.EndAtomId == endAtomId || b.StartAtomId == endAtomId);
        if (existing is not null) return;

        Execute(new AddBondCommand(startAtomId, endAtomId, order));
    }

    public void MoveAtoms(IEnumerable<(int atomId, Vector2 oldPos, Vector2 newPos)> moves)
    {
        var list = moves.ToList();
        if (list.Count == 0) return;
        Execute(new MoveAtomsCommand(list));
    }

    public void AddRing(RingTemplate template, Vector2 center)
    {
        Execute(new AddRingCommand(template, center));
    }

    /// <summary>切换键类型：单→双→三→单（Ketcher bondChangingAction 逻辑）。芳香键不参与循环。</summary>
    public void CycleBondOrder(int bondId)
    {
        if (Document.GetBond(bondId) is not { } bond) return;
        if (bond.IsAromatic) return;
        int nextOrder = bond.Order >= 3 ? 1 : bond.Order + 1;
        Execute(new ChangeBondOrderCommand(bondId, nextOrder));
    }

    public void NewDocument()
    {
        Document.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        NotifyChanged();
    }

    private void NotifyChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);
}
