using Ketcher.WinUI3.Core.Chemistry;

namespace Ketcher.WinUI3.Core.Commands;

/// <summary>修改键级命令（单→双→三循环切换）。</summary>
public sealed class ChangeBondOrderCommand : IEditorCommand
{
    private readonly int _bondId;
    private readonly int _newOrder;
    private int _oldOrder;

    public ChangeBondOrderCommand(int bondId, int newOrder)
    {
        _bondId = bondId;
        _newOrder = newOrder;
    }

    public void Execute(MoleculeDocument doc)
    {
        if (doc.GetBond(_bondId) is not { } bond) return;
        _oldOrder = bond.Order;
        bond.Order = _newOrder;
    }

    public void Undo(MoleculeDocument doc)
    {
        if (doc.GetBond(_bondId) is { } bond)
            bond.Order = _oldOrder;
    }
}
