using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;
using SkiaSharp;

namespace Ketcher.WinUI3.Rendering;

/// <summary>
/// 分子渲染器。使用 SkiaSharp 绘制原子和键。
/// 只读取 DocumentSnapshot，不修改文档。
/// </summary>
public sealed class MoleculeRenderer
{
    private const float BondWidth = 2.0f;
    private const float AtomLabelFontSize = 16f;
    private const double AtomHitRadius = 0.4;
    private const double BondHitRadius = 0.32;
    private const float SelectionRadius = 10f;

    private readonly SKTypeface _typeface;

    public MoleculeRenderer()
    {
        _typeface = SKTypeface.Default;
    }

    /// <summary>将分子快照绘制到画布。</summary>
    public void Draw(SKCanvas canvas, DocumentSnapshot snapshot, ViewportTransform viewport, float width, float height,
        HashSet<int>? selectedAtoms = null, Vector2? bondPreviewStart = null, Vector2? bondPreviewEnd = null)
    {
        canvas.Clear(new SKColor(0xFF, 0xFA, 0xFA, 0xFA));

        if (snapshot.Atoms.Count == 0 && bondPreviewStart is null)
            return;

        var atomPositions = new Dictionary<int, Vector2>();
        foreach (var atom in snapshot.Atoms)
            atomPositions[atom.Id] = atom.Position;

        // 绘制键
        foreach (var bond in snapshot.Bonds)
        {
            if (!atomPositions.TryGetValue(bond.StartAtomId, out var start) ||
                !atomPositions.TryGetValue(bond.EndAtomId, out var end))
                continue;
            DrawBond(canvas, viewport, start, end, bond);
        }

        // 绘制键创建预览线（拖拽中）
        if (bondPreviewStart is Vector2 bpStart && bondPreviewEnd is Vector2 bpEnd)
        {
            var s = viewport.ToScreen(bpStart);
            var e = viewport.ToScreen(bpEnd);
            float len = (float)Math.Sqrt(Math.Pow((float)(e.X - s.X), 2) + Math.Pow((float)(e.Y - s.Y), 2));
            if (len > 2f)
            {
                using var dashPaint = new SKPaint
                {
                    Color = new SKColor(0xFF, 0x40, 0x80, 0xFF),
                    StrokeWidth = 2f,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };
                dashPaint.PathEffect = SKPathEffect.CreateDash([6f, 4f], 0);
                canvas.DrawLine((float)s.X, (float)s.Y, (float)e.X, (float)e.Y, dashPaint);
            }
        }

        // 绘制选中高亮
        if (selectedAtoms is { Count: > 0 })
        {
            foreach (var atomId in selectedAtoms)
            {
                if (!atomPositions.TryGetValue(atomId, out var pos)) continue;
                var screen = viewport.ToScreen(pos);
                float r = SelectionRadius * (float)viewport.Scale;
                using var selPaint = new SKPaint
                {
                    Color = new SKColor(0x60, 0x40, 0x80, 0xFF),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawCircle((float)screen.X, (float)screen.Y, r, selPaint);
            }
        }

        // 绘制原子标签
        foreach (var atom in snapshot.Atoms)
        {
            var screenPos = viewport.ToScreen(atom.Position);
            DrawAtomLabel(canvas, screenPos, atom);
        }
    }

    /// <summary>命中测试：返回屏幕坐标下最近的原子 ID，或 null。</summary>
    public int? HitTestAtom(DocumentSnapshot snapshot, ViewportTransform viewport, float screenX, float screenY)
    {
        var docPoint = viewport.ToDocument(new Vector2(screenX, screenY));

        int? bestAtom = null;
        double bestDist = double.MaxValue;

        foreach (var atom in snapshot.Atoms)
        {
            double dist = atom.Position.DistanceTo(docPoint);
            if (dist <= AtomHitRadius && dist < bestDist)
            {
                bestDist = dist;
                bestAtom = atom.Id;
            }
        }

        return bestAtom;
    }

    /// <summary>兼容旧调用。</summary>
    public int? HitTest(DocumentSnapshot snapshot, ViewportTransform viewport, float screenX, float screenY)
        => HitTestAtom(snapshot, viewport, screenX, screenY);

    /// <summary>命中测试：返回屏幕坐标下命中的键 ID，或 null。</summary>
    public int? HitTestBond(DocumentSnapshot snapshot, ViewportTransform viewport, float screenX, float screenY)
    {
        var docPoint = viewport.ToDocument(new Vector2(screenX, screenY));

        int? bestBond = null;
        double bestDist = double.MaxValue;

        foreach (var bond in snapshot.Bonds)
        {
            if (!snapshot.Atoms.Any(a => a.Id == bond.StartAtomId) ||
                !snapshot.Atoms.Any(a => a.Id == bond.EndAtomId))
                continue;

            var start = snapshot.Atoms.First(a => a.Id == bond.StartAtomId).Position;
            var end = snapshot.Atoms.First(a => a.Id == bond.EndAtomId).Position;

            double dist = PointToSegmentDistance(docPoint, start, end);
            if (dist <= BondHitRadius && dist < bestDist)
            {
                bestDist = dist;
                bestBond = bond.Id;
            }
        }

        return bestBond;
    }

    private static float PointToSegmentDistance(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        double lenSq = ab.LengthSquared;
        if (lenSq < 0.0001) return (float)point.DistanceTo(a);

        double t = ((point.X - a.X) * ab.X + (point.Y - a.Y) * ab.Y) / lenSq;
        t = Math.Clamp(t, 0, 1);
        var closest = a + ab * t;
        return (float)point.DistanceTo(closest);
    }

    private void DrawBond(SKCanvas canvas, ViewportTransform viewport, Vector2 start, Vector2 end, BondSnapshot bond)
    {
        var s = viewport.ToScreen(start);
        var e = viewport.ToScreen(end);

        float sx = (float)s.X, sy = (float)s.Y;
        float ex = (float)e.X, ey = (float)e.Y;

        float dx = ex - sx, dy = ey - sy;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) return;

        float nx = -dy / len, ny = dx / len;

        using var paint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x30, 0x30, 0x30),
            StrokeWidth = BondWidth,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        if (bond.IsAromatic)
        {
            canvas.DrawLine(sx, sy, ex, ey, paint);
        }
        else if (bond.Order == 1)
        {
            canvas.DrawLine(sx, sy, ex, ey, paint);
        }
        else if (bond.Order == 2)
        {
            float offset = 3.5f;
            canvas.DrawLine(sx + nx * offset, sy + ny * offset, ex + nx * offset, ey + ny * offset, paint);
            canvas.DrawLine(sx - nx * offset, sy - ny * offset, ex - nx * offset, ey - ny * offset, paint);
        }
        else if (bond.Order == 3)
        {
            float offset = 3.5f;
            canvas.DrawLine(sx, sy, ex, ey, paint);
            canvas.DrawLine(sx + nx * offset, sy + ny * offset, ex + nx * offset, ey + ny * offset, paint);
            canvas.DrawLine(sx - nx * offset, sy - ny * offset, ex - nx * offset, ey - ny * offset, paint);
        }

        if (bond.Stereo == StereoDirection.Up)
            DrawWedge(canvas, sx, sy, ex, ey, nx, ny, filled: true);
        else if (bond.Stereo == StereoDirection.Down)
            DrawWedge(canvas, sx, sy, ex, ey, nx, ny, filled: false);
    }

    private void DrawWedge(SKCanvas canvas, float sx, float sy, float ex, float ey, float nx, float ny, bool filled)
    {
        float startWidth = 2f;
        float endWidth = 8f;

        var path = new SKPath();
        path.MoveTo(sx + nx * startWidth, sy + ny * startWidth);
        path.LineTo(ex + nx * endWidth, ey + ny * endWidth);
        path.LineTo(ex - nx * endWidth, ey - ny * endWidth);
        path.LineTo(sx - nx * startWidth, sy - ny * startWidth);
        path.Close();

        using var paint = new SKPaint
        {
            Color = new SKColor(0xFF, 0x30, 0x30, 0x30),
            IsAntialias = true,
            Style = filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = 1f
        };
        canvas.DrawPath(path, paint);
    }

    private void DrawAtomLabel(SKCanvas canvas, Vector2 screenPos, AtomSnapshot atom)
    {
        string label = atom.Element;

        if (label == "C" && atom.Charge == 0 && atom.Isotope == 0 && atom.Radical == 0 && atom.ExplicitHCount == 0)
            return;

        var color = ElementColors.Get(label);

        using var font = new SKFont(_typeface, AtomLabelFontSize);
        using var textPaint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };

        float textWidth = font.MeasureText(label);
        float textHeight = AtomLabelFontSize;
        var bgRect = new SKRect(
            (float)screenPos.X - textWidth / 2 - 3,
            (float)screenPos.Y - textHeight / 2 - 3,
            (float)screenPos.X + textWidth / 2 + 3,
            (float)screenPos.Y + textHeight / 2 + 3);

        using var bgPaint = new SKPaint { Color = new SKColor(0xFF, 0xFA, 0xFA, 0xFA) };
        canvas.DrawRect(bgRect, bgPaint);

        canvas.DrawText(label, (float)screenPos.X, (float)screenPos.Y + textHeight / 3, SKTextAlign.Center, font, textPaint);
    }
}
