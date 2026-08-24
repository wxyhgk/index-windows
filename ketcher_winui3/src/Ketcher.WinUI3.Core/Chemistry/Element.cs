namespace Ketcher.WinUI3.Core.Chemistry;

/// <summary>元素符号解析工具。</summary>
public static class Element
{
    /// <summary>从 MOL 3 字符字段解析元素符号。支持单字符（C、N）和双字符（Cl、Br、Si）。</summary>
    public static string ParseSymbol(string molField)
    {
        string trimmed = molField.Trim();
        if (trimmed.Length == 0)
            return "C";

        // 双字符元素：第二个字符是小写
        if (trimmed.Length >= 2 && char.IsLower(trimmed[1]))
            return trimmed[..2];

        return trimmed[..1];
    }

    /// <summary>将元素符号格式化为 MOL 3 字符字段（右对齐）。</summary>
    public static string ToMolField(string symbol)
    {
        return symbol.PadLeft(3);
    }

    /// <summary>常用元素符号列表，用于快捷选择。</summary>
    public static readonly string[] CommonElements =
    [
        "C", "N", "O", "S", "P", "F", "Cl", "Br", "I", "B", "Si"
    ];
}
