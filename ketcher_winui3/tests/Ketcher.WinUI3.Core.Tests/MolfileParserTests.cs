using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Formats;
using Xunit;

namespace Ketcher.WinUI3.Core.Tests;

public class MolfileParserTests
{
    // MOL V2000 原子行格式（10 字符坐标）：
    // [0-9]   [10-19] [20-29] [30-32] [33-35] [36-38] [39-41] [42-44] [45-47]
    // x(10)   y(10)   z(10)   elem(3) iso(3)  chg(3)  rad(3)  H(3)    map(3)
    //
    // 键行格式：
    // [0-2] [3-5] [6-8] [9-11]
    // start end   order stereo

    [Fact]
    public void Parse_SingleAtom_ReturnsCorrectElement()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(1, doc.AtomCount);
        Assert.Equal(0, doc.BondCount);
        var atom = doc.Atoms.First();
        Assert.Equal("C", atom.Element);
        Assert.Equal(0.0, atom.Position.X, 4);
        Assert.Equal(0.0, atom.Position.Y, 4);
    }

    [Fact]
    public void Parse_TwoAtomsWithBond_ReturnsCorrectStructure()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  N  0  0  0  0  0  0\n" +
            "  1  2  1  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(2, doc.AtomCount);
        Assert.Equal(1, doc.BondCount);

        var atoms = doc.Atoms.ToList();
        Assert.Equal("C", atoms[0].Element);
        Assert.Equal("N", atoms[1].Element);
        Assert.Equal(1.5, atoms[1].Position.X, 4);

        var bond = doc.Bonds.First();
        Assert.Equal(1, bond.StartAtomId);
        Assert.Equal(2, bond.EndAtomId);
        Assert.Equal(1, bond.Order);
        Assert.False(bond.IsAromatic);
    }

    [Fact]
    public void Parse_BenzeneRing_ReturnsAromaticBonds()
    {
        string mol =
            "Benzene\n" +
            "Source\n" +
            "  6  6  0\n" +
            "    1.2000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    0.6000    1.0400    0.0000  C  0  0  0  0  0  0\n" +
            "   -0.6000    1.0400    0.0000  C  0  0  0  0  0  0\n" +
            "   -1.2000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "   -0.6000   -1.0400    0.0000  C  0  0  0  0  0  0\n" +
            "    0.6000   -1.0400    0.0000  C  0  0  0  0  0  0\n" +
            "  1  2  4  0\n" +
            "  2  3  4  0\n" +
            "  3  4  4  0\n" +
            "  4  5  4  0\n" +
            "  5  6  4  0\n" +
            "  6  1  4  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(6, doc.AtomCount);
        Assert.Equal(6, doc.BondCount);

        foreach (var bond in doc.Bonds)
        {
            Assert.True(bond.IsAromatic);
            Assert.Equal(0, bond.Order);
        }
    }

    [Fact]
    public void Parse_DoubleBond_ReturnsCorrectOrder()
    {
        string mol =
            "Ethylene\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.3400    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "  1  2  2  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var bond = doc.Bonds.First();
        Assert.Equal(2, bond.Order);
        Assert.False(bond.IsAromatic);
    }

    [Fact]
    public void Parse_TripleBond_ReturnsCorrectOrder()
    {
        string mol =
            "Acetylene\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.2000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "  1  2  3  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var bond = doc.Bonds.First();
        Assert.Equal(3, bond.Order);
    }

    [Fact]
    public void Parse_TwoCharacterElements_ReturnsCorrectSymbols()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  3  2  0\n" +
            "    0.0000    0.0000    0.0000 Cl  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000 Br  0  0  0  0  0  0\n" +
            "    3.0000    0.0000    0.0000 Si  0  0  0  0  0  0\n" +
            "  1  2  1  0\n" +
            "  2  3  1  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var atoms = doc.Atoms.ToList();
        Assert.Equal("Cl", atoms[0].Element);
        Assert.Equal("Br", atoms[1].Element);
        Assert.Equal("Si", atoms[2].Element);
    }

    [Fact]
    public void Parse_ChargedAtom_ReturnsCorrectCharge()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  N  0  1  0  0  0  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var atom = doc.Atoms.First();
        Assert.Equal("N", atom.Element);
        Assert.Equal(1, atom.Charge);
    }

    [Fact]
    public void Parse_NegativeCharge_ReturnsCorrectCharge()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  O  0 -1 0  0  0  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var atom = doc.Atoms.First();
        Assert.Equal("O", atom.Element);
        Assert.Equal(-1, atom.Charge);
    }

    [Fact]
    public void Parse_IsotopicAtom_ReturnsCorrectIsotope()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  C 13 0  0  0  0  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var atom = doc.Atoms.First();
        Assert.Equal(13, atom.Isotope);
    }

    [Fact]
    public void Parse_ExplicitHydrogens_ReturnsCorrectCount()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  3  0  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var atom = doc.Atoms.First();
        Assert.Equal(3, atom.ExplicitHCount);
    }

    [Fact]
    public void Parse_EmptyMolecule_ReturnsZeroAtoms()
    {
        string mol =
            "Empty\n" +
            "Source\n" +
            "  0  0  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(0, doc.AtomCount);
        Assert.Equal(0, doc.BondCount);
    }

    [Fact]
    public void Parse_MultipleComponents_ReturnsDisconnectedAtoms()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  4  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    5.0000    5.0000    0.0000  N  0  0  0  0  0  0\n" +
            "    6.5000    5.0000    0.0000  O  0  0  0  0  0  0\n" +
            "  1  2  1  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(4, doc.AtomCount);
        Assert.Equal(1, doc.BondCount);
    }

    [Fact]
    public void Parse_CRLFLineEndings_ParsesCorrectly()
    {
        string mol = "Test\r\nSource\r\n  1  0  0\r\n    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\r\n$$$$\r\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(1, doc.AtomCount);
        Assert.Equal("C", doc.Atoms.First().Element);
    }

    [Fact]
    public void Parse_TrailingEmptyLines_ParsesCorrectly()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n" +
            "\n" +
            "\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(1, doc.AtomCount);
    }

    [Fact]
    public void Parse_InvalidAtomCount_ThrowsParseException()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  2  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n";

        Assert.Throws<MolfileParseException>(() => MolfileParser.Parse(mol));
    }

    [Fact]
    public void Parse_InvalidBondAtomIndex_ThrowsParseException()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  N  0  0  0  0  0  0\n" +
            "  1  5  1  0\n" +
            "$$$$\n";

        Assert.Throws<MolfileParseException>(() => MolfileParser.Parse(mol));
    }

    [Fact]
    public void Parse_InvalidCoordinate_ThrowsParseException()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "  abcdef    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n";

        Assert.Throws<MolfileParseException>(() => MolfileParser.Parse(mol));
    }

    [Fact]
    public void Parse_StereoBond_ReturnsCorrectDirection()
    {
        string mol =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  N  0  0  0  0  0  0\n" +
            "  1  2  1  1\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        var bond = doc.Bonds.First();
        Assert.Equal(StereoDirection.Up, bond.Stereo);
    }

    [Fact]
    public void Parse_WideFormat_30CharCoordinates_ParsesCorrectly()
    {
        // RDKit 输出的 30 字符坐标格式（右对齐）
        // x(30) y(30) z(30) elem(3) iso(3) chg(3) rad(3) H(3) map(3) = 108 chars
        string atom1 = "0.0000".PadLeft(30) + "0.8660".PadLeft(30) + "0.0000".PadLeft(30) + "  C" + "  0" + "  0" + "  0" + "  0" + "  0";
        string atom2 = "1.5000".PadLeft(30) + "0.8660".PadLeft(30) + "0.0000".PadLeft(30) + "  N" + "  0" + "  0" + "  0" + "  0" + "  0";

        string mol =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            atom1 + "\n" +
            atom2 + "\n" +
            "  1  2  1  0\n" +
            "$$$$\n";

        var doc = MolfileParser.Parse(mol);

        Assert.Equal(2, doc.AtomCount);
        var atoms = doc.Atoms.ToList();
        Assert.Equal("C", atoms[0].Element);
        Assert.Equal(0.0, atoms[0].Position.X, 4);
        Assert.Equal(0.866, atoms[0].Position.Y, 3);
        Assert.Equal(1.5, atoms[1].Position.X, 4);
        Assert.Equal(0.866, atoms[1].Position.Y, 3);
    }
}
