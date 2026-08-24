using Ketcher.WinUI3.Core.Chemistry;

namespace Ketcher.WinUI3.Core.Formats;

/// <summary>MOL V2000 写入器。</summary>
public static class MolfileWriter
{
    /// <summary>将分子文档写为 MOL V2000 文本。</summary>
    public static string Write(MoleculeDocument doc, MolfileVersion version = MolfileVersion.V2000)
    {
        var sb = new System.Text.StringBuilder();
        var atoms = doc.Atoms.ToList();
        var bonds = doc.Bonds.ToList();

        // 行 1：名称（最长 60 字符）
        string name = (doc.Name ?? "").Truncate(60);
        sb.AppendLine(name.PadRight(60));

        // 行 2：来源
        sb.AppendLine("  Ketcher.WinUI3".PadRight(60));

        // 行 3：计数
        sb.AppendLine(FormatCount(atoms.Count, bonds.Count, 0));

        // 原子块
        foreach (var atom in atoms)
        {
            sb.Append(atom.Position.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture).PadLeft(10));
            sb.Append(atom.Position.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture).PadLeft(10));
            sb.Append("0.0000".PadLeft(10));
            sb.Append(Element.ToMolField(atom.Element));
            sb.Append(atom.Isotope.ToString().PadLeft(3));
            sb.Append(atom.Charge.ToString().PadLeft(3));
            sb.Append(atom.Radical.ToString().PadLeft(3));
            sb.Append(atom.ExplicitHCount.ToString().PadLeft(3));
            sb.Append(atom.MappingNumber.ToString().PadLeft(3));
            sb.AppendLine();
        }

        // 键块
        foreach (var bond in bonds)
        {
            sb.Append(bond.StartAtomId.ToString().PadLeft(3));
            sb.Append(bond.EndAtomId.ToString().PadLeft(3));
            sb.Append(bond.ToMolOrder().ToString().PadLeft(3));
            sb.Append(((int)bond.Stereo).ToString().PadLeft(3));
            sb.AppendLine();
        }

        // M 记录
        WriteChargeRecord(sb, atoms);
        WriteIsotopeRecord(sb, atoms);

        // 结束标记
        sb.AppendLine("$$$$");

        return sb.ToString();
    }

    /// <summary>将分子文档写为 SDF（单记录）文本，保留 properties。</summary>
    public static string WriteSdf(MoleculeDocument doc)
    {
        var sb = new System.Text.StringBuilder();
        var atoms = doc.Atoms.ToList();
        var bonds = doc.Bonds.ToList();

        // 行 1：名称
        string name = (doc.Name ?? "").Truncate(60);
        sb.AppendLine(name.PadRight(60));

        // 行 2：来源
        sb.AppendLine("  Ketcher.WinUI3".PadRight(60));

        // 行 3：计数
        sb.AppendLine(FormatCount(atoms.Count, bonds.Count, 0));

        // 行 4：SDF 额外计数行
        sb.AppendLine(FormatCount(0, 0, 0));

        // 原子块
        foreach (var atom in atoms)
        {
            sb.Append(atom.Position.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture).PadLeft(10));
            sb.Append(atom.Position.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture).PadLeft(10));
            sb.Append("0.0000".PadLeft(10));
            sb.Append(Element.ToMolField(atom.Element));
            sb.Append(atom.Isotope.ToString().PadLeft(3));
            sb.Append(atom.Charge.ToString().PadLeft(3));
            sb.Append(atom.Radical.ToString().PadLeft(3));
            sb.Append(atom.ExplicitHCount.ToString().PadLeft(3));
            sb.Append(atom.MappingNumber.ToString().PadLeft(3));
            sb.AppendLine();
        }

        // 键块
        foreach (var bond in bonds)
        {
            sb.Append(bond.StartAtomId.ToString().PadLeft(3));
            sb.Append(bond.EndAtomId.ToString().PadLeft(3));
            sb.Append(bond.ToMolOrder().ToString().PadLeft(3));
            sb.Append(((int)bond.Stereo).ToString().PadLeft(3));
            sb.AppendLine();
        }

        // M 记录
        WriteChargeRecord(sb, atoms);
        WriteIsotopeRecord(sb, atoms);

        // 结束标记
        sb.AppendLine("$$$$");

        // SDF property records
        foreach (var (key, value) in doc.Properties)
        {
            sb.Append(key.PadRight(60));
            sb.Append(value);
            sb.AppendLine();
        }

        sb.AppendLine("$$$$");

        return sb.ToString();
    }

    private static string FormatCount(int atoms, int bonds, int stereo)
    {
        return atoms.ToString().PadRight(3) + bonds.ToString().PadRight(3) + stereo.ToString().PadRight(3);
    }

    private static void WriteChargeRecord(System.Text.StringBuilder sb, List<Atom> atoms)
    {
        var charged = atoms.Where(a => a.Charge != 0).ToList();
        if (charged.Count == 0) return;

        sb.Append("M  CHG");
        sb.Append(charged.Count.ToString().PadRight(4));
        foreach (var atom in charged)
        {
            sb.Append(atom.Id.ToString().PadRight(4));
            sb.Append(atom.Charge.ToString().PadRight(4));
        }
        sb.AppendLine();
    }

    private static void WriteIsotopeRecord(System.Text.StringBuilder sb, List<Atom> atoms)
    {
        var isotopic = atoms.Where(a => a.Isotope != 0).ToList();
        if (isotopic.Count == 0) return;

        sb.Append("M  ISO");
        sb.Append(isotopic.Count.ToString().PadRight(4));
        foreach (var atom in isotopic)
        {
            sb.Append(atom.Id.ToString().PadRight(4));
            sb.Append(atom.Isotope.ToString().PadRight(4));
        }
        sb.AppendLine();
    }
}

file static class StringExtensions
{
    public static string Truncate(this string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength];
}
