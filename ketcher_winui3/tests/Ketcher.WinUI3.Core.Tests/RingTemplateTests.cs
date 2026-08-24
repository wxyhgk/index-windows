using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Commands;
using Ketcher.WinUI3.Core.Geometry;
using Xunit;

namespace Ketcher.WinUI3.Core.Tests;

public class RingTemplateTests
{
    [Fact]
    public void Benzene_Has6Atoms()
    {
        Assert.Equal(6, RingTemplate.Benzene.AtomCount);
        Assert.Equal(6, RingTemplate.Benzene.Atoms.Count);
        Assert.Equal(6, RingTemplate.Benzene.Bonds.Count);
    }

    [Fact]
    public void Benzene_AllAtomsAreCarbon()
    {
        foreach (var (pos, element) in RingTemplate.Benzene.Atoms)
            Assert.Equal("C", element);
    }

    [Fact]
    public void Benzene_UsesKekuleRepresentation()
    {
        // Ketcher 苯环默认用 Kekulé 表示（交替单键/双键），不是芳香键
        int singleCount = 0, doubleCount = 0;
        foreach (var (start, end, order, aromatic) in RingTemplate.Benzene.Bonds)
        {
            Assert.False(aromatic);
            if (order == 1) singleCount++;
            else if (order == 2) doubleCount++;
        }
        Assert.Equal(3, singleCount);
        Assert.Equal(3, doubleCount);
    }

    [Fact]
    public void Benzene_AtomsAreEquidistantFromCenter()
    {
        double expectedRadius = 1.0; // Ketcher 键长标准化为 1.0
        foreach (var (pos, _) in RingTemplate.Benzene.Atoms)
        {
            double dist = Math.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
            Assert.True(Math.Abs(dist - expectedRadius) < 0.01, $"距离 {dist} 应约等于 {expectedRadius}");
        }
    }

    [Fact]
    public void Cyclohexane_AllBondsAreSingle()
    {
        foreach (var (start, end, order, aromatic) in RingTemplate.Cyclohexane.Bonds)
        {
            Assert.Equal(1, order);
            Assert.False(aromatic);
        }
    }

    [Fact]
    public void Cyclopentane_Has5Atoms()
    {
        Assert.Equal(5, RingTemplate.Cyclopentane.AtomCount);
    }

    [Fact]
    public void Cyclobutane_Has4Atoms()
    {
        Assert.Equal(4, RingTemplate.Cyclobutane.AtomCount);
    }

    [Fact]
    public void Cyclopropane_Has3Atoms()
    {
        Assert.Equal(3, RingTemplate.Cyclopropane.AtomCount);
    }

    [Fact]
    public void Pyridine_Has5CarbonsAnd1Nitrogen()
    {
        int carbonCount = 0, nitrogenCount = 0;
        foreach (var (_, element) in RingTemplate.Pyridine.Atoms)
        {
            if (element == "C") carbonCount++;
            else if (element == "N") nitrogenCount++;
        }
        Assert.Equal(5, carbonCount);
        Assert.Equal(1, nitrogenCount);
    }

    [Fact]
    public void All_Contains9Templates()
    {
        Assert.Equal(9, RingTemplate.All.Count);
    }

    [Fact]
    public void AddRingCommand_AddsBenzeneToDocument()
    {
        var editor = new MoleculeEditor();
        editor.AddRing(RingTemplate.Benzene, new Vector2(0, 0));

        Assert.Equal(6, editor.Document.AtomCount);
        Assert.Equal(6, editor.Document.BondCount);

        foreach (var atom in editor.Document.Atoms)
            Assert.Equal("C", atom.Element);
    }

    [Fact]
    public void AddRingCommand_UndoRemovesRing()
    {
        var editor = new MoleculeEditor();
        editor.AddRing(RingTemplate.Benzene, new Vector2(0, 0));
        Assert.Equal(6, editor.Document.AtomCount);

        editor.Undo();
        Assert.Equal(0, editor.Document.AtomCount);
        Assert.Equal(0, editor.Document.BondCount);
    }

    [Fact]
    public void AddRingCommand_RedoRestoresRing()
    {
        var editor = new MoleculeEditor();
        editor.AddRing(RingTemplate.Benzene, new Vector2(0, 0));
        editor.Undo();
        Assert.Equal(0, editor.Document.AtomCount);

        editor.Redo();
        Assert.Equal(6, editor.Document.AtomCount);
        Assert.Equal(6, editor.Document.BondCount);
    }

    [Fact]
    public void AddRingCommand_OffsetPosition()
    {
        var editor = new MoleculeEditor();
        var center = new Vector2(10, 20);
        editor.AddRing(RingTemplate.Benzene, center);

        foreach (var atom in editor.Document.Atoms)
        {
            double dx = atom.Position.X - center.X;
            double dy = atom.Position.Y - center.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            Assert.True(Math.Abs(dist - 1.0) < 0.01, $"原子应在中心 (10,20) 周围半径 1.0 处");
        }
    }
}
