using Ketcher.WinUI3.Core.Chemistry;
using Ketcher.WinUI3.Core.Commands;
using Ketcher.WinUI3.Core.Geometry;
using Ketcher.WinUI3.Core.Formats;
using Ketcher.WinUI3.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Input;
using Windows.UI.Core;

namespace Ketcher.WinUI3.Controls;

/// <summary>分子编辑工具。</summary>
public enum MoleculeTool
{
    Select,
    AddAtom,
    AddBond,
    AddRing,
    Delete
}

/// <summary>
/// 分子画布控件。使用 SkiaSharp 渲染，支持编辑、平移、缩放。
/// </summary>
public sealed class MoleculeCanvasControl : UserControl
{
    private readonly SKXamlCanvas _canvas;
    private readonly MoleculeRenderer _renderer;
    private readonly MoleculeEditor _editor;
    private readonly ViewportTransform _viewport;

    private MoleculeTool _tool = MoleculeTool.Select;
    private string _currentElement = "C";
    private int _currentBondOrder = 1;
    private RingTemplate _currentRingTemplate = RingTemplate.Benzene;
    private readonly HashSet<int> _selectedAtoms = new();

    private Point _lastPointerPos;
    private bool _isPanning;
    private bool _isDragging;
    private bool _ctrlPressed;
    private readonly List<DragMove> _dragMoves = new();

    // Bond 拖拽状态
    private bool _isBondDragging;
    private int? _bondDragStartAtom;
    private Vector2? _bondDragStartPos;
    private Vector2? _bondDragCurrentPos;

    private sealed record DragMove(int AtomId, Vector2 StartPos, Vector2 CurrentPos);

    public MoleculeCanvasControl()
    {
        _editor = new MoleculeEditor();
        _renderer = new MoleculeRenderer();
        _viewport = new ViewportTransform();

        _canvas = new SKXamlCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _canvas.PaintSurface += OnPaintSurface;

        Content = _canvas;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheel;
        SizeChanged += OnSizeChanged;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;

        _editor.DocumentChanged += (_, _) => _canvas.Invalidate();
    }

    public MoleculeEditor Editor => _editor;
    public MoleculeDocument Document => _editor.Document;
    public MoleculeTool Tool { get => _tool; set { _tool = value; } }
    public string CurrentElement { get => _currentElement; set => _currentElement = value; }
    public int CurrentBondOrder { get => _currentBondOrder; set => _currentBondOrder = value; }
    public RingTemplate CurrentRingTemplate { get => _currentRingTemplate; set => _currentRingTemplate = value; }
    public IReadOnlyCollection<int> SelectedAtoms => _selectedAtoms;

    public void LoadMolfile(string molfile)
    {
        var doc = MolfileParser.Parse(molfile);
        TransferDocument(doc);
    }

    public void LoadSdf(string sdf)
    {
        var doc = MolfileParser.ParseSdf(sdf);
        TransferDocument(doc);
    }

    public string SaveMolfile() => MolfileWriter.Write(_editor.Document);
    public string SaveSdf() => MolfileWriter.WriteSdf(_editor.Document);

    public void NewDocument()
    {
        _editor.NewDocument();
        _selectedAtoms.Clear();
        _viewport.Reset();
        _canvas.Invalidate();
    }

    public void Undo() => _editor.Undo();
    public void Redo() => _editor.Redo();

    public void FitToWindowPublic() => FitToWindow();

    public void ZoomIn()
    {
        _viewport.Zoom(1.25, _canvas.ActualWidth / 2, _canvas.ActualHeight / 2);
        _canvas.Invalidate();
    }

    public void ZoomOut()
    {
        _viewport.Zoom(0.8, _canvas.ActualWidth / 2, _canvas.ActualHeight / 2);
        _canvas.Invalidate();
    }

    private void TransferDocument(MoleculeDocument source)
    {
        _editor.Document.Clear();
        foreach (var atom in source.Atoms)
        {
            var newAtom = _editor.Document.AddAtom(atom.Element, atom.Position);
            newAtom.Charge = atom.Charge;
            newAtom.Isotope = atom.Isotope;
            newAtom.Radical = atom.Radical;
            newAtom.ExplicitHCount = atom.ExplicitHCount;
            newAtom.MappingNumber = atom.MappingNumber;
        }
        foreach (var bond in source.Bonds)
        {
            var newBond = _editor.Document.AddBond(bond.StartAtomId, bond.EndAtomId, bond.Order);
            newBond.IsAromatic = bond.IsAromatic;
            newBond.Stereo = bond.Stereo;
        }
        foreach (var (key, value) in source.Properties)
            _editor.Document.Properties.Add(new KeyValuePair<string, string>(key, value));

        _selectedAtoms.Clear();
        InvalidateCanvas();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var info = e.Info;
        if (info.Width <= 0 || info.Height <= 0) return;

        var snapshot = _editor.Document.CreateSnapshot();

        DocumentSnapshot displaySnapshot = snapshot;
        if (_isDragging && _dragMoves.Count > 0)
        {
            var tempDoc = new MoleculeDocument();
            foreach (var atom in snapshot.Atoms)
            {
                var pos = _dragMoves.FirstOrDefault(m => m.AtomId == atom.Id)?.CurrentPos ?? atom.Position;
                var a = tempDoc.AddAtom(atom.Element, pos);
                a.Charge = atom.Charge;
                a.Isotope = atom.Isotope;
                a.Radical = atom.Radical;
                a.ExplicitHCount = atom.ExplicitHCount;
                a.MappingNumber = atom.MappingNumber;
            }
            foreach (var bond in snapshot.Bonds)
            {
                var b = tempDoc.AddBond(bond.StartAtomId, bond.EndAtomId, bond.Order);
                b.IsAromatic = bond.IsAromatic;
                b.Stereo = bond.Stereo;
            }
            displaySnapshot = tempDoc.CreateSnapshot();
        }

        // Bond 拖拽预览
        Vector2? bondPreviewStart = null, bondPreviewEnd = null;
        if (_isBondDragging && _bondDragStartPos is Vector2 startPos && _bondDragCurrentPos is Vector2 curPos)
        {
            bondPreviewStart = startPos;
            bondPreviewEnd = curPos;
        }

        _renderer.Draw(
            e.Surface.Canvas,
            displaySnapshot,
            _viewport,
            info.Width,
            info.Height,
            _selectedAtoms.Count > 0 ? _selectedAtoms : null,
            bondPreviewStart,
            bondPreviewEnd);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        _lastPointerPos = pos;

        var snapshot = _editor.Document.CreateSnapshot();
        var hitAtom = _renderer.HitTest(snapshot, _viewport, (float)pos.X, (float)pos.Y);
        bool isCtrl = _ctrlPressed;

        switch (_tool)
        {
            case MoleculeTool.Select:
                if (hitAtom is int atomId)
                {
                    if (isCtrl)
                    {
                        if (_selectedAtoms.Contains(atomId)) _selectedAtoms.Remove(atomId);
                        else _selectedAtoms.Add(atomId);
                    }
                    else
                    {
                        _selectedAtoms.Clear();
                        _selectedAtoms.Add(atomId);
                    }
                    _isDragging = true;
                    _dragMoves.Clear();
                    foreach (var selId in _selectedAtoms)
                    {
                        if (_editor.Document.GetAtom(selId) is { } atom)
                            _dragMoves.Add(new DragMove(selId, atom.Position, atom.Position));
                    }
                    CapturePointer(e.Pointer);
                }
                else
                {
                    // 检查是否点击了键（Ketcher：点击已有键循环切换 单→双→三）
                    var hitBond = _renderer.HitTestBond(snapshot, _viewport, (float)pos.X, (float)pos.Y);
                    if (hitBond is int bondId)
                    {
                        _editor.CycleBondOrder(bondId);
                        _selectedAtoms.Clear();
                        _canvas.Invalidate();
                        break;
                    }

                    _selectedAtoms.Clear();
                    _isPanning = true;
                    CapturePointer(e.Pointer);
                }
                _canvas.Invalidate();
                break;

            case MoleculeTool.AddAtom:
                if (hitAtom is null)
                {
                    var docPos = _viewport.ToDocument(new Vector2(pos.X, pos.Y));
                    _editor.AddAtom(_currentElement, docPos);
                }
                break;

            case MoleculeTool.AddBond:
                // Ketcher BondTool：
                // - 命中键 → 切换键类型
                // - 命中原子 → 开始拖拽（从原子拉键）
                // - 空白 → 开始拖拽（从空白拉键）
                var hitBondAtPress = _renderer.HitTestBond(snapshot, _viewport, (float)pos.X, (float)pos.Y);
                if (hitBondAtPress is int bondIdAtPress)
                {
                    _editor.CycleBondOrder(bondIdAtPress);
                    break;
                }

                _isBondDragging = true;
                _bondDragStartAtom = hitAtom;
                _bondDragStartPos = hitAtom is int startAtomId && _editor.Document.GetAtom(startAtomId) is { } startAtom
                    ? startAtom.Position
                    : _viewport.ToDocument(new Vector2(pos.X, pos.Y));
                _bondDragCurrentPos = _bondDragStartPos;
                CapturePointer(e.Pointer);
                break;

            case MoleculeTool.AddRing:
                if (hitAtom is null)
                {
                    var ringCenter = _viewport.ToDocument(new Vector2(pos.X, pos.Y));
                    _editor.AddRing(_currentRingTemplate, ringCenter);
                }
                break;

            case MoleculeTool.Delete:
                if (hitAtom is int delAtomId)
                {
                    _editor.RemoveAtom(delAtomId);
                    _selectedAtoms.Remove(delAtomId);
                }
                else if (_selectedAtoms.Count > 0)
                {
                    foreach (var id in _selectedAtoms.ToList())
                        _editor.RemoveAtom(id);
                    _selectedAtoms.Clear();
                }
                break;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var currentPos = point.Position;

        if (_isBondDragging)
        {
            _bondDragCurrentPos = _viewport.ToDocument(new Vector2(currentPos.X, currentPos.Y));
            _canvas.Invalidate();
            return;
        }

        if (_isPanning)
        {
            double dx = currentPos.X - _lastPointerPos.X;
            double dy = currentPos.Y - _lastPointerPos.Y;
            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
            {
                _viewport.Pan(dx, dy);
                _lastPointerPos = currentPos;
                _canvas.Invalidate();
            }
            return;
        }

        if (_isDragging)
        {
            double dx = currentPos.X - _lastPointerPos.X;
            double dy = currentPos.Y - _lastPointerPos.Y;
            if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
            {
                var delta = new Vector2(dx / _viewport.Scale, dy / _viewport.Scale);
                for (int i = 0; i < _dragMoves.Count; i++)
                {
                    var m = _dragMoves[i];
                    _dragMoves[i] = new DragMove(m.AtomId, m.StartPos, m.StartPos + delta);
                }
                _lastPointerPos = currentPos;
                _canvas.Invalidate();
            }
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isBondDragging)
        {
            var point = e.GetCurrentPoint(this);
            var releasePos = _viewport.ToDocument(new Vector2(point.Position.X, point.Position.Y));
            FinishBondDrag(releasePos);
            _isBondDragging = false;
            _bondDragStartAtom = null;
            _bondDragStartPos = null;
            _bondDragCurrentPos = null;
            ReleasePointerCapture(e.Pointer);
            return;
        }

        if (_isDragging)
        {
            var moves = _dragMoves
                .Where(m => m.StartPos.X != m.CurrentPos.X || m.StartPos.Y != m.CurrentPos.Y)
                .Select(m => (m.AtomId, m.StartPos, m.CurrentPos))
                .ToList();
            if (moves.Count > 0)
                _editor.MoveAtoms(moves);
            _dragMoves.Clear();
        }

        _isPanning = false;
        _isDragging = false;
        ReleasePointerCapture(e.Pointer);
    }

    /// <summary>完成 Bond 拖拽：根据起点和终点类型创建键（Ketcher BondTool.mouseup 逻辑）。</summary>
    private void FinishBondDrag(Vector2 releaseDocPos)
    {
        var snapshot = _editor.Document.CreateSnapshot();
        var hitAtom = _renderer.HitTestAtom(snapshot, _viewport,
            (float)_viewport.ToScreen(releaseDocPos).X,
            (float)_viewport.ToScreen(releaseDocPos).Y);

        Vector2 startPos = _bondDragStartPos ?? releaseDocPos;
        double dist = startPos.DistanceTo(releaseDocPos);
        int? startAtomId = _bondDragStartAtom;

        // 距离太短视为"单击"（Ketcher: dist <= 0.3 时走单击逻辑）
        if (dist < 0.3)
        {
            if (startAtomId is int clickAtomId)
            {
                // 单击原子：从该原子拉出一条键到新 C
                var newAtomPos = CalcNewAtomPosition(
                    _editor.Document.GetAtom(clickAtomId)!.Position,
                    releaseDocPos);
                var newAtom = _editor.AddAtom("C", newAtomPos);
                _editor.AddBond(clickAtomId, newAtom.Id, _currentBondOrder);
            }
            else
            {
                // 单击空白：创建 C-C 键（Ketcher: 在鼠标附近创建两个碳原子）
                var v = new Vector2(0.5, 0);
                var a1Pos = releaseDocPos - v;
                var a2Pos = releaseDocPos + v;
                var atom1 = _editor.AddAtom("C", a1Pos);
                var atom2 = _editor.AddAtom("C", a2Pos);
                _editor.AddBond(atom1.Id, atom2.Id, _currentBondOrder);
            }
            return;
        }

        // 拖拽：根据起点和终点类型创建键
        if (startAtomId is int sa && hitAtom is int ea)
        {
            // 原子 → 原子：创建键
            if (sa != ea)
                _editor.AddBond(sa, ea, _currentBondOrder);
        }
        else if (startAtomId is int sa2 && hitAtom is null)
        {
            // 原子 → 空白：创建新碳原子 + 键
            var newAtomPos = CalcNewAtomPosition(startPos, releaseDocPos);
            var newAtom = _editor.AddAtom("C", newAtomPos);
            _editor.AddBond(sa2, newAtom.Id, _currentBondOrder);
        }
        else if (startAtomId is null && hitAtom is int ea2)
        {
            // 空白 → 原子：创建新碳原子 + 键
            var newAtomPos = CalcNewAtomPosition(startPos, releaseDocPos);
            var newAtom = _editor.AddAtom("C", newAtomPos);
            _editor.AddBond(newAtom.Id, ea2, _currentBondOrder);
        }
        else if (startAtomId is null && hitAtom is null)
        {
            // 空白 → 空白：创建两个新碳原子 + 键
            var mid = (startPos + releaseDocPos) * 0.5;
            var halfDir = (releaseDocPos - startPos) * 0.5;
            var a1Pos = mid - halfDir;
            var a2Pos = mid + halfDir;
            var atom1 = _editor.AddAtom("C", a1Pos);
            var atom2 = _editor.AddAtom("C", a2Pos);
            _editor.AddBond(atom1.Id, atom2.Id, _currentBondOrder);
        }
    }

    /// <summary>
    /// 计算新原子位置（Ketcher calcNewAtomPos）：
    /// 从起点沿拖拽方向，距离 1 键长，角度 snap 到 15° 网格。
    /// Ctrl 按住时使用精确角度（不 snap）。
    /// </summary>
    private static Vector2 CalcNewAtomPosition(Vector2 from, Vector2 to, bool ctrlKey = false)
    {
        var dir = to - from;
        double len = dir.Length;
        if (len < 0.001)
            return from + new Vector2(1, 0);

        double angle = Math.Atan2(dir.Y, dir.X);
        if (!ctrlKey)
        {
            // Ketcher: FRAC = Math.PI / 12 (15°)
            const double step = Math.PI / 12;
            angle = Math.Round(angle / step) * step;
        }

        const double bondLength = 1.0;
        return from + new Vector2(Math.Cos(angle) * bondLength, Math.Sin(angle) * bondLength);
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        double delta = point.Properties.MouseWheelDelta;
        double factor = delta > 0 ? 1.1 : 1.0 / 1.1;
        _viewport.Zoom(factor, pos.X, pos.Y);
        _canvas.Invalidate();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.LeftControl || e.Key == VirtualKey.RightControl)
            _ctrlPressed = true;

        bool isCtrl = _ctrlPressed;

        if (e.Key == VirtualKey.Delete)
        {
            foreach (var id in _selectedAtoms.ToList())
                _editor.RemoveAtom(id);
            _selectedAtoms.Clear();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Z && isCtrl)
        {
            _editor.Undo();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Y && isCtrl)
        {
            _editor.Redo();
            e.Handled = true;
        }
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.LeftControl || e.Key == VirtualKey.RightControl)
            _ctrlPressed = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_editor.Document.AtomCount > 0 && _viewport.Scale == 1.0 && _viewport.OffsetX == 0 && _viewport.OffsetY == 0)
            FitToWindow();
    }

    private void InvalidateCanvas()
    {
        if (_canvas.ActualWidth > 0 && _canvas.ActualHeight > 0)
            FitToWindow();
        else
            _canvas.Invalidate();
    }

    private void FitToWindow()
    {
        if (_editor.Document.AtomCount == 0) return;
        var points = _editor.Document.Atoms.Select(a => (a.Position.X, a.Position.Y)).ToList();
        var (min, max) = ViewportTransform.GetContentBounds(points);
        _viewport.FitToContent(min, max, _canvas.ActualWidth, _canvas.ActualHeight);
        _canvas.Invalidate();
    }
}
