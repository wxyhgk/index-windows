using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Formats;
using Ketcher.WinUI3.Core.Geometry;
using Ketcher.WinUI3.Rendering;
using SkiaSharp;
using Xunit;

namespace Ketcher.WinUI3.Rendering.Tests;

public class MoleculeRendererTests
{
    private static DocumentSnapshot CreateSnapshot(string mol)
    {
        var doc = MolfileParser.Parse(mol);
        return doc.CreateSnapshot();
    }

    private static string MakeMol(int atomCount, int bondCount, string[] atomLines, string[] bondLines)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Test");
        sb.AppendLine("Source");
        sb.AppendLine($"  {atomCount}  {bondCount}  0");
        foreach (var line in atomLines) sb.AppendLine(line);
        foreach (var line in bondLines) sb.AppendLine(line);
        sb.AppendLine("$$$$");
        return sb.ToString();
    }

    [Fact]
    public void Draw_EmptySnapshot_DoesNotThrow()
    {
        var snapshot = new MoleculeDocument().CreateSnapshot();
        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        var renderer = new MoleculeRenderer();
        var viewport = new ViewportTransform();

        renderer.Draw(surface.Canvas, snapshot, viewport, 400, 300);
    }

    [Fact]
    public void Draw_SingleAtom_ProducesNonWhitePixels()
    {
        string mol = MakeMol(1, 0,
            ["    0.0000    0.0000    0.0000  N  0  0  0  0  0  0"],
            []);

        var snapshot = CreateSnapshot(mol);
        var viewport = new ViewportTransform();
        viewport.FitToContent(new Vector2(0, 0), new Vector2(0, 0), 400, 300, margin: 0);

        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        var renderer = new MoleculeRenderer();
        renderer.Draw(surface.Canvas, snapshot, viewport, 400, 300);

        var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var centerColor = bitmap.GetPixel(200, 150);
        Assert.False(centerColor.Red == 255 && centerColor.Green == 255 && centerColor.Blue == 255,
            "Center pixel should not be pure white when an atom is drawn");
    }

    [Fact]
    public void Draw_Bond_ProducesNonWhitePixels()
    {
        string mol = MakeMol(2, 1,
            ["    0.0000    0.0000    0.0000  C  0  0  0  0  0  0",
             "    1.5000    0.0000    0.0000  C  0  0  0  0  0  0"],
            ["  1  2  1  0"]);

        var snapshot = CreateSnapshot(mol);
        var viewport = new ViewportTransform();
        var points = snapshot.Atoms.Select(a => (a.Position.X, a.Position.Y)).ToList();
        var (min, max) = ViewportTransform.GetContentBounds(points);
        viewport.FitToContent(min, max, 400, 300, margin: 50);

        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        var renderer = new MoleculeRenderer();
        renderer.Draw(surface.Canvas, snapshot, viewport, 400, 300);

        var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        bool hasNonWhite = false;
        for (int x = 100; x < 300; x += 5)
        {
            for (int y = 100; y < 200; y += 5)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 250 || pixel.Green < 250 || pixel.Blue < 250)
                {
                    hasNonWhite = true;
                    break;
                }
            }
            if (hasNonWhite) break;
        }
        Assert.True(hasNonWhite, "Bond line should produce non-white pixels");
    }

    [Fact]
    public void HitTest_AtomCenter_ReturnsAtomId()
    {
        string mol = MakeMol(2, 1,
            ["    0.0000    0.0000    0.0000  N  0  0  0  0  0  0",
             "    1.5000    0.0000    0.0000  O  0  0  0  0  0  0"],
            ["  1  2  1  0"]);

        var snapshot = CreateSnapshot(mol);
        var viewport = new ViewportTransform();
        var points = snapshot.Atoms.Select(a => (a.Position.X, a.Position.Y)).ToList();
        var (min, max) = ViewportTransform.GetContentBounds(points);
        viewport.FitToContent(min, max, 400, 300, margin: 50);

        var renderer = new MoleculeRenderer();
        var nScreen = viewport.ToScreen(new Vector2(0, 0));
        var hit = renderer.HitTest(snapshot, viewport, (float)nScreen.X, (float)nScreen.Y);

        Assert.NotNull(hit);
        Assert.Equal(1, hit);
    }

    [Fact]
    public void HitTest_FarFromAtoms_ReturnsNull()
    {
        string mol = MakeMol(1, 0,
            ["    0.0000    0.0000    0.0000  N  0  0  0  0  0  0"],
            []);

        var snapshot = CreateSnapshot(mol);
        var viewport = new ViewportTransform();
        viewport.FitToContent(new Vector2(0, 0), new Vector2(0, 0), 400, 300, margin: 0);

        var renderer = new MoleculeRenderer();
        var hit = renderer.HitTest(snapshot, viewport, 10, 10);

        Assert.Null(hit);
    }

    [Fact]
    public void Draw_DoubleBond_ProducesMoreNonWhiteThanSingle()
    {
        string single = MakeMol(2, 1,
            ["    0.0000    0.0000    0.0000  C  0  0  0  0  0  0",
             "    1.5000    0.0000    0.0000  C  0  0  0  0  0  0"],
            ["  1  2  1  0"]);

        string double_ = MakeMol(2, 1,
            ["    0.0000    0.0000    0.0000  C  0  0  0  0  0  0",
             "    1.5000    0.0000    0.0000  C  0  0  0  0  0  0"],
            ["  1  2  2  0"]);

        var viewport = new ViewportTransform();
        var points = new List<(double, double)> { (0, 0), (1.5, 0) };
        var (min, max) = ViewportTransform.GetContentBounds(points);
        viewport.FitToContent(min, max, 400, 300, margin: 50);

        int CountNonWhite(DocumentSnapshot snapshot)
        {
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            var renderer = new MoleculeRenderer();
            renderer.Draw(surface.Canvas, snapshot, viewport, 400, 300);
            var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            int count = 0;
            for (int x = 0; x < 400; x++)
                for (int y = 0; y < 300; y++)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.Red < 250 || p.Green < 250 || p.Blue < 250)
                        count++;
                }
            return count;
        }

        int singleCount = CountNonWhite(CreateSnapshot(single));
        int doubleCount = CountNonWhite(CreateSnapshot(double_));

        Assert.True(doubleCount > singleCount,
            $"Double bond should have more non-white pixels ({doubleCount}) than single ({singleCount})");
    }
}
