using Ketcher.WinUI3.Core.Chemistry;

namespace Ketcher.WinUI3.Core.Commands;

/// <summary>可撤销的编辑器命令。</summary>
public interface IEditorCommand
{
    void Execute(MoleculeDocument doc);
    void Undo(MoleculeDocument doc);
}
