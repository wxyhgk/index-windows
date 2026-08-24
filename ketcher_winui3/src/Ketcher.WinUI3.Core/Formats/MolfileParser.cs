using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;

namespace Ketcher.WinUI3.Core.Formats;

/// <summary>
/// MOL V2000 文件解析器。使用逐行 tokenizer 处理固定宽度字段，
/// 支持 CRLF/LF 换行、尾部空行和非 ASCII 属性文本。
/// </summary>
public static class MolfileParser
{
    private const int AtomLineLength = 90;
    private const int BondLineLength = 90;

    /// <summary>解析 MOL V2000 或 SDF（单记录）文本为分子文档。</summary>
    public static MoleculeDocument Parse(string text)
    {
        var lines = NormalizeLineEndings(text);
        var doc = new MoleculeDocument();

        int lineIndex = 0;

        // 行 1-3：头部
        if (lines.Count < 3)
            throw new MolfileParseException("File too short: expected at least 3 header lines", 0);

        doc.Name = lines[0].Trim();
        lineIndex = 2; // 跳过行 1（名称）和行 2（来源）

        // 行 3：计数行
        var countLine = lines[lineIndex];
        lineIndex++;
        int atomCount = ParseCount(countLine, 0, 3, "atom count", lineIndex + 1);
        int bondCount = ParseCount(countLine, 3, 6, "bond count", lineIndex + 1);

        // 原子块
        for (int i = 0; i < atomCount; i++)
        {
            if (lineIndex >= lines.Count)
                throw new MolfileParseException($"Expected atom line {i + 1} of {atomCount}, but file ended", lineIndex + 1);

            var atomLine = lines[lineIndex];
            lineIndex++;
            var atom = ParseAtomLine(atomLine, i + 1, lineIndex);
            doc.AddAtomInternal(atom);
        }

        // 键块
        for (int i = 0; i < bondCount; i++)
        {
            if (lineIndex >= lines.Count)
                throw new MolfileParseException($"Expected bond line {i + 1} of {bondCount}, but file ended", lineIndex + 1);

            var bondLine = lines[lineIndex];
            lineIndex++;
            var bond = ParseBondLine(bondLine, i + 1, lineIndex, atomCount);
            doc.AddBondInternal(bond);
        }

        // M 记录（可选）
        while (lineIndex < lines.Count)
        {
            var line = lines[lineIndex];
            if (line.StartsWith("$$$$"))
                break;

            if (line.StartsWith("M  "))
            {
                ParseMRecord(line, doc, lineIndex + 1);
            }
            // 其他未知记录行：忽略（保留原始文本用于 SDF properties）
            lineIndex++;
        }

        return doc;
    }

    /// <summary>解析 SDF 文件（单记录），保留 property records。</summary>
    public static MoleculeDocument ParseSdf(string text)
    {
        var lines = NormalizeLineEndings(text);

        // SDF 格式：
        // 行 1: 化合物名称
        // 行 2: 来源
        // 行 3: 计数
        // 行 4: 计数（SDF 多一行）
        // 行 5+: 原子/键块
        // M 记录
        // $$$$
        // property records（key-value 对）
        // $$$$

        if (lines.Count < 4)
            throw new MolfileParseException("SDF file too short: expected at least 4 header lines", 0);

        var doc = new MoleculeDocument();
        doc.Name = lines[0].Trim();

        // 行 3（index 2）：计数
        var countLine = lines[2];
        int atomCount = ParseCount(countLine, 0, 3, "atom count", 3);
        int bondCount = ParseCount(countLine, 3, 6, "bond count", 3);

        // SDF 行 4（index 3）是额外的计数行，跳过
        int lineIndex = 4;

        // 原子块
        for (int i = 0; i < atomCount; i++)
        {
            if (lineIndex >= lines.Count)
                throw new MolfileParseException($"Expected atom line {i + 1} of {atomCount}, but file ended", lineIndex + 1);
            var atom = ParseAtomLine(lines[lineIndex], i + 1, lineIndex + 1);
            doc.AddAtomInternal(atom);
            lineIndex++;
        }

        // 键块
        for (int i = 0; i < bondCount; i++)
        {
            if (lineIndex >= lines.Count)
                throw new MolfileParseException($"Expected bond line {i + 1} of {bondCount}, but file ended", lineIndex + 1);
            var bond = ParseBondLine(lines[lineIndex], i + 1, lineIndex + 1, atomCount);
            doc.AddBondInternal(bond);
            lineIndex++;
        }

        // M 记录直到 $$$$
        while (lineIndex < lines.Count)
        {
            var line = lines[lineIndex];
            if (line.StartsWith("$$$$"))
            {
                lineIndex++;
                break;
            }
            if (line.StartsWith("M  "))
                ParseMRecord(line, doc, lineIndex + 1);
            lineIndex++;
        }

        // SDF property records
        while (lineIndex < lines.Count)
        {
            var line = lines[lineIndex];
            if (line.StartsWith("$$$$"))
                break;

            // property record 格式：key value（第一个空格分隔 key 和 value）
            if (line.Length > 0)
            {
                int spaceIndex = line.IndexOf(' ');
                string key, value;
                if (spaceIndex > 0)
                {
                    key = line[..spaceIndex];
                    value = line[(spaceIndex + 1)..];
                }
                else
                {
                    key = line;
                    value = string.Empty;
                }
                if (!string.IsNullOrEmpty(key))
                    doc.Properties.Add(new KeyValuePair<string, string>(key, value.Trim()));
            }
            lineIndex++;
        }

        return doc;
    }

    private static Atom ParseAtomLine(string line, int expectedIndex, int lineNumber)
    {
        // MOL V2000 支持两种坐标宽度：
        // 10 字符格式（标准）：X[0-9] Y[10-19] Z[20-29] Element[30-32] ...
        // 30 字符格式（RDKit 扩展）：X[0-29] Y[30-59] Z[60-89] Element[90-92] ...
        // 通过行长度检测：30 字符格式行长度 >= 93
        bool wideFormat = line.Length >= 93;

        int xStart, xEnd, yStart, yEnd, zStart, zEnd, elemStart;
        if (wideFormat)
        {
            xStart = 0; xEnd = 30;
            yStart = 30; yEnd = 60;
            zStart = 60; zEnd = 90;
            elemStart = 90;
        }
        else
        {
            xStart = 0; xEnd = 10;
            yStart = 10; yEnd = 20;
            zStart = 20; zEnd = 30;
            elemStart = 30;
        }

        int minLen = wideFormat ? 93 : 36;
        if (line.Length < minLen)
            throw new MolfileParseException(
                $"Atom line {expectedIndex} too short ({line.Length} chars, need at least {minLen})", lineNumber);

        double x = ParseCoordinate(line, xStart, xEnd, "x", lineNumber);
        double y = ParseCoordinate(line, yStart, yEnd, "y", lineNumber);
        double z = ParseCoordinate(line, zStart, zEnd, "z", lineNumber);
        string element = Element.ParseSymbol(line[elemStart..(elemStart + 3)]);
        int isotope = ParseIntField(line, elemStart + 3, elemStart + 6, "isotope", lineNumber);
        int charge = ParseIntField(line, elemStart + 6, elemStart + 9, "charge", lineNumber);
        int radical = ParseIntField(line, elemStart + 9, elemStart + 12, "radical", lineNumber);
        int explicitH = ParseIntField(line, elemStart + 12, elemStart + 15, "explicit H", lineNumber);
        int mapping = ParseIntField(line, elemStart + 15, elemStart + 18, "mapping", lineNumber);

        return new Atom(expectedIndex, element, new Vector2(x, y))
        {
            Isotope = isotope,
            Charge = charge,
            Radical = radical,
            ExplicitHCount = explicitH,
            MappingNumber = mapping
        };
    }

    private static Bond ParseBondLine(string line, int expectedIndex, int lineNumber, int atomCount)
    {
        if (line.Length < 12)
            throw new MolfileParseException(
                $"Bond line {expectedIndex} too short ({line.Length} chars, need at least 12)", lineNumber);

        int startAtom = ParseIntField(line, 0, 3, "start atom", lineNumber);
        int endAtom = ParseIntField(line, 3, 6, "end atom", lineNumber);
        int order = ParseIntField(line, 6, 9, "bond order", lineNumber);
        int stereo = ParseIntField(line, 9, 12, "stereo", lineNumber);

        if (startAtom < 1 || startAtom > atomCount)
            throw new MolfileParseException($"Bond {expectedIndex}: start atom {startAtom} out of range [1, {atomCount}]", lineNumber);
        if (endAtom < 1 || endAtom > atomCount)
            throw new MolfileParseException($"Bond {expectedIndex}: end atom {endAtom} out of range [1, {atomCount}]", lineNumber);

        var (parsedOrder, aromatic) = Bond.ParseOrder(order);
        var stereoDir = ParseStereo(stereo);

        return new Bond(expectedIndex, startAtom, endAtom, parsedOrder)
        {
            IsAromatic = aromatic,
            Stereo = stereoDir
        };
    }

    private static void ParseMRecord(string line, MoleculeDocument doc, int lineNumber)
    {
        // M 记录格式：M   SUBTYPE  ...
        // 例如 M  CHG  1  1  0
        if (line.Length < 6) return;

        string subtype = line[4..6].Trim();
        switch (subtype)
        {
            case "CHG":
                // 电荷：M  CHG  n  idx1 chg1 idx2 chg2 ...
                ParseChargeRecord(line, doc, lineNumber);
                break;
            case "ISO":
                // 同位素：M  ISO  n  idx1 iso1 idx2 iso2 ...
                ParseIsotopeRecord(line, doc, lineNumber);
                break;
            // 其他 M 记录类型暂不处理
        }
    }

    private static void ParseChargeRecord(string line, MoleculeDocument doc, int lineNumber)
    {
        var parts = line[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return;

        if (!int.TryParse(parts[0], out int count)) return;
        for (int i = 0; i < count && 1 + i * 2 < parts.Length; i++)
        {
            if (int.TryParse(parts[1 + i * 2], out int idx) &&
                int.TryParse(parts[2 + i * 2], out int chg) &&
                doc.GetAtom(idx) is { } atom)
            {
                atom.Charge = chg;
            }
        }
    }

    private static void ParseIsotopeRecord(string line, MoleculeDocument doc, int lineNumber)
    {
        var parts = line[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return;

        if (!int.TryParse(parts[0], out int count)) return;
        for (int i = 0; i < count && 1 + i * 2 < parts.Length; i++)
        {
            if (int.TryParse(parts[1 + i * 2], out int idx) &&
                int.TryParse(parts[2 + i * 2], out int iso) &&
                doc.GetAtom(idx) is { } atom)
            {
                atom.Isotope = iso;
            }
        }
    }

    private static StereoDirection ParseStereo(int value) => value switch
    {
        1 => StereoDirection.Up,
        6 => StereoDirection.Down,
        7 => StereoDirection.Either,
        8 => StereoDirection.Neither,
        _ => StereoDirection.None
    };

    private static double ParseCoordinate(string line, int start, int end, string name, int lineNumber)
    {
        string field = line[start..Math.Min(end, line.Length)];
        if (!double.TryParse(field, out double value))
            throw new MolfileParseException($"Invalid {name} coordinate '{field.Trim()}'", lineNumber, name);
        return value;
    }

    private static int ParseCount(string line, int start, int end, string name, int lineNumber)
    {
        string field = line[start..Math.Min(end, line.Length)];
        if (!int.TryParse(field.Trim(), out int value))
            throw new MolfileParseException($"Invalid {name} '{field.Trim()}'", lineNumber, name);
        if (value < 0)
            throw new MolfileParseException($"Negative {name} {value}", lineNumber, name);
        return value;
    }

    private static int ParseIntField(string line, int start, int end, string name, int lineNumber)
    {
        string field = line[start..Math.Min(end, line.Length)];
        if (!int.TryParse(field.Trim(), out int value))
            return 0; // 空白字段视为 0
        return value;
    }

    /// <summary>统一换行符为 \n，保留尾部空行信息。</summary>
    private static List<string> NormalizeLineEndings(string text)
    {
        // 处理 CRLF、CR、LF
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n', StringSplitOptions.None).ToList();

        // 移除尾部空行（但保留有意义的空行）
        while (lines.Count > 0 && lines[^1].Trim() == "")
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }
}
