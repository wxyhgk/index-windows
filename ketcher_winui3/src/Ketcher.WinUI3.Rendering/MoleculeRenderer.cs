using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Geometry;
using SkiaSharp;

namespace Ketcher.WinUI3.Rendering;

/// <summary>
/// 分子渲染器。使用 SkiaSharp 绘制原子和键。
/// 所有渲染参数基于模型单位（键长=1），绘制时乘以 viewport.Scale 转为屏幕像素。
/// Ketcher 默认 microModeScale=100，画布值 = 模型值 × 100。
/// </summary>
public sealed class MoleculeRenderer
{
    // Ketcher 渲染参数（模型单位 = 画布值 / microModeScale(100)）
    private const double BondLineWidth = 0.05;       // 5px / 100
    private const double AtomLabelFontSize = 0.32;   // 32px / 100
    private const double BondSpace = 0.1429;         // 14.29px / 100 (100/7/100)
    private const double SubFontSize = 0.16;         // 16px / 100

    // 命中检测（模型单位）
    private const double AtomHitRadius = 0.4;
    private const double BondHitRadius = 0.32;

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

        float scale = (float)viewport.Scale;
        float lineWidth = (float)(BondLineWidth * scale);
        float fontSize = (float)(AtomLabelFontSize * scale);
        float bondSpace = (float)(BondSpace * scale);

        // 计算原子标签半宽（屏幕像素，用于键端点缩短）
        // 计算每个原子的连接键数（芳香键按 1 计）
        var connectionCounts = new Dictionary<int, int>();
        foreach (var atom in snapshot.Atoms)
            connectionCounts[atom.Id] = 0;
        foreach (var bond in snapshot.Bonds)
        {
            if (connectionCounts.ContainsKey(bond.StartAtomId))
                connectionCounts[bond.StartAtomId] += bond.IsAromatic ? 1 : bond.Order;
            if (connectionCounts.ContainsKey(bond.EndAtomId))
                connectionCounts[bond.EndAtomId] += bond.IsAromatic ? 1 : bond.Order;
        }

        var atomLabelHalfWidths = new Dictionary<int, float>();
        foreach (var atom in snapshot.Atoms)
        {
            atomLabelHalfWidths[atom.Id] = GetLabelHalfWidth(atom, connectionCounts[atom.Id], fontSize);
        }

        // 绘制键（端点基于标签 bbox 缩短，Ketcher getShiftedSegmentPosition 逻辑）
        foreach (var bond in snapshot.Bonds)
        {
            if (!snapshot.Atoms.Any(a => a.Id == bond.StartAtomId) ||
                !snapshot.Atoms.Any(a => a.Id == bond.EndAtomId))
                continue;
            var start = snapshot.Atoms.First(a => a.Id == bond.StartAtomId).Position;
            var end = snapshot.Atoms.First(a => a.Id == bond.EndAtomId).Position;
            float startShift = atomLabelHalfWidths[bond.StartAtomId];
            float endShift = atomLabelHalfWidths[bond.EndAtomId];
            DrawBond(canvas, viewport, start, end, bond, startShift, endShift, lineWidth, bondSpace);
        }

        // 绘制原子标签（Ketcher: 标签在 bondSkeleton 层之上，盖在键上）
        foreach (var atom in snapshot.Atoms)
        {
            var screenPos = viewport.ToScreen(atom.Position);
            int connCount = connectionCounts[atom.Id];
            DrawAtomLabel(canvas, screenPos, atom, connCount, fontSize);
        }

        // 绘制选中高亮
        if (selectedAtoms is { Count: > 0 })
        {
            foreach (var atomId in selectedAtoms)
            {
                var atom = snapshot.Atoms.FirstOrDefault(a => a.Id == atomId);
                if (!snapshot.Atoms.Any(a => a.Id == atomId)) continue;
                var screen = viewport.ToScreen(atom.Position);
                float r = 0.5f * scale;
                using var selPaint = new SKPaint
                {
                    Color = new SKColor(0x60, 0x40, 0x80, 0xFF),
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawCircle((float)screen.X, (float)screen.Y, r, selPaint);
            }
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
                    StrokeWidth = lineWidth,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };
                dashPaint.PathEffect = SKPathEffect.CreateDash([6f, 4f], 0);
                canvas.DrawLine((float)s.X, (float)s.Y, (float)e.X, (float)e.Y, dashPaint);
            }
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

    private void DrawBond(SKCanvas canvas, ViewportTransform viewport, Vector2 start, Vector2 end, BondSnapshot bond,
        float startShift, float endShift, float lineWidth, float bondSpace)
    {
        var s = viewport.ToScreen(start);
        var e = viewport.ToScreen(end);

        float sx = (float)s.X, sy = (float)s.Y;
        float ex = (float)e.X, ey = (float)e.Y;

        float dx = ex - sx, dy = ey - sy;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) return;

        float ux = dx / len, uy = dy / len;
        float nx = -uy, ny = ux;

        // Ketcher: 键端点从原子中心沿键方向推出标签 bbox 边缘 + 3*lineWidth
        // Ketcher getRatio: 短键时按比例缩小偏移
        float sShift = startShift + 3f * lineWidth;
        float eShift = endShift + 3f * lineWidth;

        if (sShift + eShift >= len)
        {
            float ratio = len / (sShift + eShift);
            sShift *= ratio * 0.5f;
            eShift *= ratio * 0.5f;
        }

        float sx2 = sx + ux * sShift, sy2 = sy + uy * sShift;
        float ex2 = ex - ux * eShift, ey2 = ey - uy * eShift;

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = lineWidth,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        if (bond.IsAromatic)
        {
            canvas.DrawLine(sx2, sy2, ex2, ey2, paint);
        }
        else if (bond.Order == 1)
        {
            canvas.DrawLine(sx2, sy2, ex2, ey2, paint);
        }
        else if (bond.Order == 2)
        {
            // Ketcher: 双键 = 两条平行线，偏移 = bondSpace/2
            float offset = bondSpace / 2f;
            canvas.DrawLine(sx2 + nx * offset, sy2 + ny * offset, ex2 + nx * offset, ey2 + ny * offset, paint);
            canvas.DrawLine(sx2 - nx * offset, sy2 - ny * offset, ex2 - nx * offset, ey2 - ny * offset, paint);
        }
        else if (bond.Order == 3)
        {
            // Ketcher: 三键 = 主线 + ±bondSpace 两条偏移线
            float offset = bondSpace;
            canvas.DrawLine(sx2, sy2, ex2, ey2, paint);
            canvas.DrawLine(sx2 + nx * offset, sy2 + ny * offset, ex2 + nx * offset, ey2 + ny * offset, paint);
            canvas.DrawLine(sx2 - nx * offset, sy2 - ny * offset, ex2 - nx * offset, ey2 - ny * offset, paint);
        }

        if (bond.Stereo == StereoDirection.Up)
            DrawWedge(canvas, sx2, sy2, ex2, ey2, nx, ny, lineWidth, filled: true);
        else if (bond.Stereo == StereoDirection.Down)
            DrawWedge(canvas, sx2, sy2, ex2, ey2, nx, ny, lineWidth, filled: false);
    }

    private void DrawWedge(SKCanvas canvas, float sx, float sy, float ex, float ey, float nx, float ny, float lineWidth, bool filled)
    {
        float startWidth = lineWidth;
        float endWidth = lineWidth * 3f;

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
            StrokeWidth = lineWidth
        };
        canvas.DrawPath(path, paint);
    }

    private void DrawAtomLabel(SKCanvas canvas, Vector2 screenPos, AtomSnapshot atom, int connCount, float fontSize)
    {
        string element = atom.Element;
        float subFontSize = fontSize * 0.5f;

        // 计算隐氢（Ketcher TerminalAndHetero：末端碳和杂原子显示）
        int implicitH = ValenceRules.CalcImplicitH(element, atom.Charge, atom.Radical, connCount);
        if (atom.ExplicitHCount > 0)
            implicitH = atom.ExplicitHCount;

        bool isCarbon = element == "C";
        bool isTerminal = connCount <= 1;
        bool showMainLabel = !isCarbon || atom.Charge != 0 || atom.Isotope != 0 || atom.Radical != 0 || atom.ExplicitHCount > 0;
        bool showHydrogen = implicitH > 0 && (isTerminal || !isCarbon);

        // 绘制主标签
        if (showMainLabel)
        {
            var color = ElementColors.Get(element);
            using var font = new SKFont(_typeface, fontSize);
            using var textPaint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawText(element, (float)screenPos.X, (float)screenPos.Y + fontSize / 3, SKTextAlign.Center, font, textPaint);
        }

        // 绘制隐氢（Ketcher: "H" + 计数，在原子标签右侧）
        if (showHydrogen)
        {
            string hLabel = implicitH > 1 ? $"H{implicitH}" : "H";
            using var font = new SKFont(_typeface, fontSize);
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            float offsetX = showMainLabel ? fontSize * 0.7f : 0f;
            canvas.DrawText(hLabel, (float)screenPos.X + offsetX, (float)screenPos.Y + fontSize / 3, SKTextAlign.Left, font, textPaint);
        }

        // 绘制电荷（Ketcher: 右上角 "+" 或 "−"）
        if (atom.Charge != 0)
        {
            string chargeLabel = atom.Charge > 0 ? "+" : "\u2013";
            using var font = new SKFont(_typeface, subFontSize);
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            float offsetX = showMainLabel ? fontSize * 0.6f : 0f;
            float offsetY = -fontSize * 0.3f;
            canvas.DrawText(chargeLabel, (float)screenPos.X + offsetX, (float)screenPos.Y + offsetY, SKTextAlign.Left, font, textPaint);
        }

        // 绘制同位素（Ketcher: 左上角数字）
        if (atom.Isotope != 0)
        {
            string isoLabel = atom.Isotope.ToString();
            using var font = new SKFont(_typeface, subFontSize);
            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            float offsetX = -fontSize * 0.6f;
            float offsetY = -fontSize * 0.3f;
            canvas.DrawText(isoLabel, (float)screenPos.X + offsetX, (float)screenPos.Y + offsetY, SKTextAlign.Right, font, textPaint);
        }
    }

    /// <summary>计算原子标签的总半宽（屏幕像素，用于键端点缩短）。包含主标签+隐氢+电荷。</summary>
    private float GetLabelHalfWidth(AtomSnapshot atom, int connCount, float fontSize)
    {
        string element = atom.Element;
        float subFontSize = fontSize * 0.5f;

        int implicitH = ValenceRules.CalcImplicitH(element, atom.Charge, atom.Radical, connCount);
        if (atom.ExplicitHCount > 0)
            implicitH = atom.ExplicitHCount;

        bool isCarbon = element == "C";
        bool isTerminal = connCount <= 1;
        bool showMainLabel = !isCarbon || atom.Charge != 0 || atom.Isotope != 0 || atom.Radical != 0 || atom.ExplicitHCount > 0;
        bool showHydrogen = implicitH > 0 && (isTerminal || !isCarbon);

        if (!showMainLabel && !showHydrogen && atom.Charge == 0 && atom.Isotope == 0)
            return 0f;

        using var font = new SKFont(_typeface, fontSize);
        float totalWidth = 0f;

        if (showMainLabel)
            totalWidth += font.MeasureText(element);

        if (showHydrogen)
        {
            string hLabel = implicitH > 1 ? $"H{implicitH}" : "H";
            totalWidth += font.MeasureText(hLabel);
        }

        if (atom.Charge != 0)
        {
            using var subFont = new SKFont(_typeface, subFontSize);
            string chargeLabel = atom.Charge > 0 ? "+" : "\u2013";
            totalWidth += subFont.MeasureText(chargeLabel);
        }

        return totalWidth / 2f;
    }
}
