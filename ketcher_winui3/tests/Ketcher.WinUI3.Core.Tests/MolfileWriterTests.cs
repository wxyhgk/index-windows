using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Formats;
using Ketcher.WinUI3.Core.Geometry;
using Xunit;

namespace Ketcher.WinUI3.Core.Tests;

public class MolfileWriterTests
{
    // MOL V2000 原子行（10 字符坐标，右对齐）：
    // x(10) y(10) z(10) elem(3) iso(3) chg(3) rad(3) H(3) map(3)
    // 键行：start(3) end(3) order(3) stereo(3)

    [Fact]
    public void Write_ParsedMolfile_RoundTripPreservesStructure()
    {
        string original =
            "Test Molecule\n" +
            "Source\n" +
            "  3  2  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.8660    0.0000  N  0  0  0  0  0  0\n" +
            "    1.5000   -0.8660    0.0000  O  0  0  0  0  0  0\n" +
            "  1  2  1  0\n" +
            "  2  3  2  0\n" +
            "$$$$\n";

        var doc1 = MolfileParser.Parse(original);
        string written = MolfileWriter.Write(doc1);
        var doc2 = MolfileParser.Parse(written);

        Assert.Equal(doc1.AtomCount, doc2.AtomCount);
        Assert.Equal(doc1.BondCount, doc2.BondCount);

        var atoms1 = doc1.Atoms.OrderBy(a => a.Id).ToList();
        var atoms2 = doc2.Atoms.OrderBy(a => a.Id).ToList();

        for (int i = 0; i < atoms1.Count; i++)
        {
            Assert.Equal(atoms1[i].Element, atoms2[i].Element);
            Assert.Equal(atoms1[i].Position.X, atoms2[i].Position.X, 4);
            Assert.Equal(atoms1[i].Position.Y, atoms2[i].Position.Y, 4);
        }

        var bonds1 = doc1.Bonds.OrderBy(b => b.Id).ToList();
        var bonds2 = doc2.Bonds.OrderBy(b => b.Id).ToList();

        for (int i = 0; i < bonds1.Count; i++)
        {
            Assert.Equal(bonds1[i].StartAtomId, bonds2[i].StartAtomId);
            Assert.Equal(bonds1[i].EndAtomId, bonds2[i].EndAtomId);
            Assert.Equal(bonds1[i].Order, bonds2[i].Order);
            Assert.Equal(bonds1[i].IsAromatic, bonds2[i].IsAromatic);
        }
    }

    [Fact]
    public void Write_AromaticBonds_RoundTripPreservesAromaticity()
    {
        string original =
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

        var doc1 = MolfileParser.Parse(original);
        string written = MolfileWriter.Write(doc1);
        var doc2 = MolfileParser.Parse(written);

        Assert.Equal(6, doc2.BondCount);
        foreach (var bond in doc2.Bonds)
        {
            Assert.True(bond.IsAromatic);
        }
    }

    [Fact]
    public void Write_ChargedAtom_RoundTripPreservesCharge()
    {
        string original =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  N  0  1  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  O  0 -1 0  0  0  0\n" +
            "  1  2  1  0\n" +
            "$$$$\n";

        var doc1 = MolfileParser.Parse(original);
        string written = MolfileWriter.Write(doc1);
        var doc2 = MolfileParser.Parse(written);

        var atoms = doc2.Atoms.OrderBy(a => a.Id).ToList();
        Assert.Equal(1, atoms[0].Charge);
        Assert.Equal(-1, atoms[1].Charge);
    }

    [Fact]
    public void Write_TwoCharacterElements_RoundTripPreservesSymbols()
    {
        string original =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000 Cl  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000 Br  0  0  0  0  0  0\n" +
            "  1  2  1  0\n" +
            "$$$$\n";

        var doc1 = MolfileParser.Parse(original);
        string written = MolfileWriter.Write(doc1);
        var doc2 = MolfileParser.Parse(written);

        var atoms = doc2.Atoms.OrderBy(a => a.Id).ToList();
        Assert.Equal("Cl", atoms[0].Element);
        Assert.Equal("Br", atoms[1].Element);
    }

    [Fact]
    public void WriteSdf_WithProperties_RoundTripPreservesProperties()
    {
        string original =
            "Test Molecule\n" +
            "Source\n" +
            "  1  0  0\n" +
            "  0  0  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "$$$$\n" +
            "MolGrapherConfidence 0.95\n" +
            "ProcessingTimeMs 1234\n" +
            "$$$$\n";

        var doc1 = MolfileParser.ParseSdf(original);
        string written = MolfileWriter.WriteSdf(doc1);
        var doc2 = MolfileParser.ParseSdf(written);

        Assert.Equal(doc1.Properties.Count, doc2.Properties.Count);
        Assert.Contains(doc2.Properties, p => p.Key == "MolGrapherConfidence" && p.Value == "0.95");
        Assert.Contains(doc2.Properties, p => p.Key == "ProcessingTimeMs" && p.Value == "1234");
    }

    [Fact]
    public void Write_EmptyDocument_ProducesValidMolfile()
    {
        var doc = new MoleculeDocument();
        string written = MolfileWriter.Write(doc);

        var reparsed = MolfileParser.Parse(written);
        Assert.Equal(0, reparsed.AtomCount);
        Assert.Equal(0, reparsed.BondCount);
    }

    [Fact]
    public void Write_StereoBonds_RoundTripPreservesStereo()
    {
        string original =
            "Test\n" +
            "Source\n" +
            "  2  1  0\n" +
            "    0.0000    0.0000    0.0000  C  0  0  0  0  0  0\n" +
            "    1.5000    0.0000    0.0000  N  0  0  0  0  0  0\n" +
            "  1  2  1  1\n" +
            "$$$$\n";

        var doc1 = MolfileParser.Parse(original);
        string written = MolfileWriter.Write(doc1);
        var doc2 = MolfileParser.Parse(written);

        Assert.Equal(StereoDirection.Up, doc2.Bonds.First().Stereo);
    }

    [Fact]
    public void Write_IsotopicAtom_RoundTripPreservesIsotope()
    {
        string original =
            "Test\n" +
            "Source\n" +
            "  1  0  0\n" +
            "    0.0000    0.0000    0.0000  C 13 0  0  0  0  0\n" +
            "$$$$\n";

        var doc1 = MolfileParser.Parse(original);
        string written = MolfileWriter.Write(doc1);
        var doc2 = MolfileParser.Parse(written);

        Assert.Equal(13, doc2.Atoms.First().Isotope);
    }
}
