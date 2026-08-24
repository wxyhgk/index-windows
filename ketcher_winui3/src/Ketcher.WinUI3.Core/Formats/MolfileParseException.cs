namespace Ketcher.WinUI3.Core.Formats;

/// <summary>MOL 文件解析错误，携带行号和字段位置信息。</summary>
public sealed class MolfileParseException : Exception
{
    public int LineNumber { get; }
    public string? FieldName { get; }

    public MolfileParseException(string message, int lineNumber, string? fieldName = null)
        : base($"Line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
        FieldName = fieldName;
    }

    public MolfileParseException(string message, Exception innerException, int lineNumber, string? fieldName = null)
        : base($"Line {lineNumber}: {message}", innerException)
    {
        LineNumber = lineNumber;
        FieldName = fieldName;
    }
}
