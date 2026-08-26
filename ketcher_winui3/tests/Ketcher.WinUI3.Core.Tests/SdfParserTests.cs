using Ketcher.WinUI3.Core.Formats;
using Xunit;

namespace Ketcher.WinUI3.Core.Tests;

public class SdfParserTests
{
    // SDF 原子行与 MOL V2000 相同：10 字符坐标 + 3 字符字段
    // 格式: x(10) y(10) z(10) elem(3) iso(3) chg(3) rad(3) H(3) map(3)

    [Fact]
    public void ParseSdf_SingleMolecule_ReturnsCorrectStructure()
    {
        string sdf =
            "Test Molecule\n" +
            "Source\n" +
            "  2  1  0\n" +
            "  0  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  N  0  0  0  0  0  0\n" +
            "  1  2  1  0\n" +
            "$$$$\n" +
            "$$$$\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Equal(2, doc.AtomCount);
        Assert.Equal(1, doc.BondCount);
        Assert.Equal("Test Molecule", doc.Name);
    }

    [Fact]
    public void ParseSdf_WithProperties_PreservesProperties()
    {
        string sdf =
            "Test Molecule\n" +
            "Source\n" +
            "  1  0  0\n" +
            "  0  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n" +
            "MolGrapherConfidence 0.95\n" +
            "ProcessingTimeMs 1234\n" +
            "$$$$\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Equal(1, doc.AtomCount);
        Assert.Equal(2, doc.Properties.Count);

        var confidence = doc.Properties.First(p => p.Key == "MolGrapherConfidence");
        Assert.Equal("0.95", confidence.Value);

        var time = doc.Properties.First(p => p.Key == "ProcessingTimeMs");
        Assert.Equal("1234", time.Value);
    }

    [Fact]
    public void ParseSdf_WithNonAsciiProperties_PreservesProperties()
    {
        string sdf =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "  0  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n" +
            "描述 这是一个测试分子\n" +
            "$$$$\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Single(doc.Properties);
        var prop = doc.Properties[0];
        Assert.Equal("描述", prop.Key);
        Assert.Equal("这是一个测试分子", prop.Value);
    }

    [Fact]
    public void ParseSdf_WithLongProperty_PreservesFullValue()
    {
        string longValue = new('A', 200);
        string sdf =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "  0  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n" +
            $"LongProp {longValue}\n" +
            "$$$$\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Single(doc.Properties);
        var prop = doc.Properties[0];
        Assert.Equal("LongProp", prop.Key);
        Assert.Equal(longValue, prop.Value);
    }

    [Fact]
    public void ParseSdf_MolGrapherOutput_PreservesCoordinates()
    {
        // 模拟 MolGrapher 输出的 SDF 格式（30 字符坐标，右对齐）
        string atom1 = "0.0000".PadLeft(30) + "0.8660".PadLeft(30) + "0.0000".PadLeft(30) + "  C" + "  0" + "  0" + "  0" + "  0" + "  0";
        string atom2 = "1.5000".PadLeft(30) + "0.8660".PadLeft(30) + "0.0000".PadLeft(30) + "  C" + "  0" + "  0" + "  0" + "  0" + "  0";
        string atom3 = "1.5000".PadLeft(30) + "-0.866".PadLeft(30) + "0.0000".PadLeft(30) + "  O" + "  0" + "  0" + "  0" + "  0" + "  0";

        string sdf =
            "MolGrapher Result\n" +
            "molgrapher\n" +
            "  3  2  0\n" +
            "  0  0  0\n" +
            atom1 + "\n" +
            atom2 + "\n" +
            atom3 + "\n" +
            "  1  2  2  0\n" +
            "  2  3  1  0\n" +
            "$$$$\n" +
            "smi CC=O\n" +
            "confidence 0.87\n" +
            "$$$$\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Equal(3, doc.AtomCount);
        Assert.Equal(2, doc.BondCount);

        var atoms = doc.Atoms.ToList();
        Assert.Equal(0.0, atoms[0].Position.X, 4);
        Assert.Equal(-0.866, atoms[0].Position.Y, 3);
        Assert.Equal(1.5, atoms[1].Position.X, 4);
        Assert.Equal(-0.866, atoms[1].Position.Y, 3);
        Assert.Equal(1.5, atoms[2].Position.X, 4);
        Assert.Equal(0.866, atoms[2].Position.Y, 3);

        Assert.Equal(2, doc.Properties.Count);
        Assert.Contains(doc.Properties, p => p.Key == "smi" && p.Value == "CC=O");
    }

    [Fact]
    public void ParseSdf_CRLFLineEndings_ParsesCorrectly()
    {
        string sdf = "Test\r\nSource\r\n  1  0  0\r\n  0  0  0\r\n    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\r\n$$$$\r\n$$$$\r\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Equal(1, doc.AtomCount);
    }

    [Fact]
    public void ParseSdf_EmptyMolecule_ReturnsZeroAtoms()
    {
        string sdf =
            "Empty\n" +
            "Source\n" +
            "  0  0  0\n" +
            "  0  0  0\n" +
            "$$$$\n" +
            "$$$$\n";

        var doc = MolfileParser.ParseSdf(sdf);

        Assert.Equal(0, doc.AtomCount);
        Assert.Equal(0, doc.BondCount);
    }
}
