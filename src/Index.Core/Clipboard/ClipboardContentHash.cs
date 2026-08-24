using System.Security.Cryptography;
using System.Text;

namespace Index.Clipboard;

/// <summary>Canonical hashes shared by clipboard readers and history replay suppression.</summary>
public static class ClipboardContentHash
{
    public static string FromText(string text)
        => FromBytes(Encoding.UTF8.GetBytes(text));

    public static string FromImagePng(ReadOnlySpan<byte> pngData)
        => FromBytes(pngData);

    public static string FromFiles(IEnumerable<string> paths)
    {
        var joined = string.Join("\n", paths.Order(StringComparer.OrdinalIgnoreCase));
        return FromBytes(Encoding.UTF8.GetBytes(joined));
    }

    public static string FromBytes(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
