namespace Index.Annotation;

/// <summary>
/// 即时标注的状态机。截图覆盖层和钉图窗口共用同一个实例类型。
/// 坐标用「画布空间」，左上原点、Y 向下。
/// 对应 macOS 端 AnnotationState。
/// </summary>
public sealed class AnnotationState
{
    // 调色板
    public static readonly LColor[] Palette =
    {
        new LColor(0.98, 0.22, 0.22, 1),   // 红
        new LColor(1.00, 0.72, 0.10, 1),   // 橙
        new LColor(0.20, 0.55, 0.98, 1),   // 蓝
        new LColor(1.00, 1.00, 1.00, 1)    // 白
    };

    public static readonly double[] Widths = { 2, 4, 8 };

    /// <summary>状态变更通知。WinUI 端用事件替代 ObservableObject。</summary>
    public event Action? Changed;

    private void PublishChange() => Changed?.Invoke();

    // 当前工具（null = 指针模式）
    private AnnotationTool? _tool;
    public AnnotationTool? Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            PublishChange();
        }
    }

    /// <summary>每个工具各记各的样式。</summary>
    private readonly Dictionary<string, ToolStyle> _toolStyles = new();

    /// <summary>画布空间相对屏幕点的倍率。</summary>
    public double StrokeScale { get; set; } = 1;

    /// <summary>画布单位到图像像素的倍率。</summary>
    public double PixelScale { get; set; } = 1;

    // 图层
    public Layers<CanvasSpace> Layers { get; private set; } = new();

    // 选中
    public Guid? SelectedId { get; private set; }

    // 绘制中
    private PointF? _dragStart;
    private PointF? _dragCurrent;
    private bool _constrainDragGeometry;

    // 移动中
    private Guid? _moveId;
    private PointF? _moveLastPoint;
    private LRect? _moveFromRect;

    // 缩放中
    public Guid? ResizeId { get; private set; }
    public ResizeHandle? ActiveResizeHandle { get; private set; }
    private Layer? _resizeOriginal;
    private PointF? _resizeStartPoint;

    // 文字编辑
    public Guid? EditingTextId { get; private set; }
    public string? MarkedText { get; set; }

    // 马赛克预览
    public Dictionary<Guid, byte[]> PixelatePreviews { get; } = new();

    // 撤销/重做
    public AnnotationHistory History { get; } = new();

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    public AnnotationState()
    {
        foreach (var descriptor in ToolRegistry.Descriptors)
        {
            var style = descriptor.DefaultStyle.Clone();
            if (descriptor.Axes.Contains(ToolStyleAxis.Color)) style.ColorIndex = 0;
            if (descriptor.Axes.Contains(ToolStyleAxis.Width)) style.WidthIndex = 1;
            _toolStyles[descriptor.Tool.Id()] = style;
        }
    }

    // MARK: - 当前样式

    public AnnotationTool? StyleTool
    {
        get
        {
            if (Tool.HasValue) return Tool;
            if (SelectedId.HasValue && Layers.FirstIndex(SelectedId.Value) is int idx)
                return AnnotationToolExtensions.FromKind(Layers.Elements[idx].Kind);
            return null;
        }
    }

    public ToolStyleAxis[] StyleAxes
    {
        get
        {
            if (!StyleTool.HasValue) return Array.Empty<ToolStyleAxis>();
            return ToolRegistry.DescriptorFor(StyleTool.Value).Axes;
        }
    }

    public ToolStyle CurrentStyle
    {
        get
        {
            if (!StyleTool.HasValue) return new ToolStyle();
            return _toolStyles.TryGetValue(StyleTool.Value.Id(), out var s)
                ? s : ToolRegistry.DescriptorFor(StyleTool.Value).DefaultStyle;
        }
    }

    public LColor Color
    {
        get
        {
            int idx = CurrentStyle.ColorIndex;
            return idx >= 0 && idx < Palette.Length ? Palette[idx] : Palette[0];
        }
    }

    public double LineWidth => CurrentStyle.ValueFor(ToolStyleAxis.Width) * StrokeScale;
    public double FontSize => CurrentStyle.ValueFor(ToolStyleAxis.FontSize) * StrokeScale;

    public bool IsEmpty => Layers.IsEmpty;
    public bool IsDrawing => _dragStart != null;

    /// <summary>改当前工具某根轴的档位。</summary>
    public bool SetStyleIndex(int index, ToolStyleAxis axis)
    {
        if (!StyleTool.HasValue) return false;
        var style = CurrentStyle.Clone();
        if (style.IndexFor(axis) == index) return false;
        style.SetIndex(index, axis);
        _toolStyles[StyleTool.Value.Id()] = style;
        PublishChange();
        return true;
    }

    // MARK: - 绘制中的图层

    private Layer? PendingLayer
    {
        get
        {
            if (!Tool.HasValue || _dragStart == null || _dragCurrent == null) return null;
            var current = _constrainDragGeometry
                ? AnnotationGeometryConstraints.ConstrainEndpoint(Tool.Value, _dragStart.Value, _dragCurrent.Value)
                : _dragCurrent.Value;
            return MakeLayer(Tool.Value, _dragStart.Value, current);
        }
    }

    /// <summary>交给渲染层的完整列表：已提交的 + 正在拖的。</summary>
    public List<Layer> DisplayLayers
    {
        get
        {
            var result = new List<Layer>(Layers.Elements);
            if (EditingTextId.HasValue && Layers.FirstIndex(EditingTextId.Value) is int idx)
            {
                if (MarkedText is { Length: > 0 })
                    result[idx] = result[idx] with { Text = result[idx].Text + MarkedText };
                else
                    result[idx] = result[idx] with { Text = result[idx].Text + "|" };
            }
            if (PendingLayer is { } pending)
                result.Add(pending);
            return result;
        }
    }

    private Layer MakeLayer(AnnotationTool tool, PointF from, PointF to)
    {
        var descriptor = ToolRegistry.DescriptorFor(tool);
        var style = _toolStyles.TryGetValue(tool.Id(), out var s) ? s : descriptor.DefaultStyle;
        int colorIdx = style.ColorIndex;
        var color = colorIdx >= 0 && colorIdx < Palette.Length ? Palette[colorIdx] : Palette[0];

        return descriptor.MakeLayer(new ToolLayerContext
        {
            From = from,
            To = to,
            Style = style,
            Color = color,
            StrokeScale = StrokeScale,
            PixelScale = PixelScale,
            Existing = Layers.Elements
        });
    }

    // MARK: - 输入

    /// <summary>落笔。点击类工具当场落层，拖拽类进入拖拽态。</summary>
    public bool BeginDraw(PointF point)
    {
        if (!Tool.HasValue) return false;
        EndTextEditing();
        Select(null);

        if (ToolRegistry.DescriptorFor(Tool.Value).Input != ToolInput.Click)
        {
            _dragStart = point;
            _dragCurrent = point;
            _constrainDragGeometry = false;
            PublishChange();
            return true;
        }

        var layer = MakeLayer(Tool.Value, point, point);
        Layers.Append(layer);

        if (Tool.Value == AnnotationTool.Text)
        {
            EditingTextId = layer.Id;
            SelectedId = layer.Id;
        }
        else
        {
            History.Record(new AnnotationHistory.Mutation.Add(layer, null));
        }
        PublishChange();
        return true;
    }

    public bool UpdateDraw(PointF point, bool constrainGeometry = false)
    {
        if (_dragStart == null) return false;
        _dragCurrent = point;
        _constrainDragGeometry = constrainGeometry;
        PublishChange();
        return true;
    }

    /// <summary>提交正在拖的图层。</summary>
    public bool EndDraw()
    {
        var layer = PendingLayer;
        _dragStart = null;
        _dragCurrent = null;
        _constrainDragGeometry = false;

        if (layer is null)
        {
            PublishChange();
            return false;
        }

        var r = layer.Rect.Standardized();
        if (r.W < 2 && r.H < 2)
        {
            PublishChange();
            return false;
        }

        // 裁剪同时只允许存在一个
        if (layer.Kind == LayerKind.Crop)
        {
            while (Layers.Elements.FindIndex(l => l.Kind == LayerKind.Crop) is int cropIdx)
            {
                var old = Layers.Elements[cropIdx];
                History.Record(new AnnotationHistory.Mutation.Remove(old, cropIdx, null));
                Layers.Remove(old.Id);
            }
        }

        Layers.Append(layer);
        History.Record(new AnnotationHistory.Mutation.Add(layer, PixelatePreviews.GetValueOrDefault(layer.Id)));
        PublishChange();
        return true;
    }

    /// <summary>丢弃全部标注。</summary>
    public void Clear()
    {
        EndTextEditing();
        SelectedId = null;
        _dragStart = null;
        _dragCurrent = null;
        _constrainDragGeometry = false;
        _moveId = null;
        _moveLastPoint = null;
        _moveFromRect = null;
        ResizeId = null;
        ActiveResizeHandle = null;
        _resizeOriginal = null;
        _resizeStartPoint = null;
        Layers = new Layers<CanvasSpace>();
        PixelatePreviews.Clear();
        History.Reset();
        PublishChange();
    }

    /// <summary>
    /// Replaces the editable canvas with a complete persisted image-space revision.
    /// The editor canvas uses image pixels as its native coordinate system, so this
    /// is an identity projection. Transient gestures, selection and undo history are reset.
    /// </summary>
    public void LoadImageLayers(Layers<ImageSpace> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        SelectedId = null;
        _dragStart = null;
        _dragCurrent = null;
        _constrainDragGeometry = false;
        _moveId = null;
        _moveLastPoint = null;
        _moveFromRect = null;
        ResizeId = null;
        ActiveResizeHandle = null;
        _resizeOriginal = null;
        _resizeStartPoint = null;
        EditingTextId = null;
        MarkedText = null;
        Layers = new Layers<CanvasSpace>();
        foreach (var layer in source.Elements)
            Layers.Append(layer with { });
        PixelatePreviews.Clear();
        History.Reset();
        PublishChange();
    }

    // MARK: - 选中与拖动

    /// <summary>命中测试，从最上层往下找。</summary>
    public Guid? LayerAt(PointF point)
    {
        double tolerance = 8 * StrokeScale;
        for (int i = Layers.Elements.Count - 1; i >= 0; i--)
        {
            var layer = Layers.Elements[i];
            if (layer.IsEffect) continue;
            var descriptor = ToolRegistry.DescriptorFor(layer.Kind);
            if (descriptor is null) continue;
            if (descriptor.HitTest(layer, point, tolerance)) return layer.Id;
        }
        return null;
    }

    public bool Select(Guid? id)
    {
        if (SelectedId == id) return false;
        SelectedId = id;
        History.BreakCoalescing();
        PublishChange();
        return true;
    }

    public void BeginMove(Guid id, PointF point)
    {
        SelectedId = id;
        _moveId = id;
        _moveLastPoint = point;
        if (Layers.FirstIndex(id) is int idx)
            _moveFromRect = Layers.Elements[idx].Rect;
        PublishChange();
    }

    public bool UpdateMove(PointF point)
    {
        if (_moveId == null || _moveLastPoint == null) return false;
        if (Layers.FirstIndex(_moveId.Value) is not int idx) return false;

        var layer = Layers.Elements[idx];
        var newRect = new LRect(
            layer.Rect.X + point.X - _moveLastPoint.Value.X,
            layer.Rect.Y + point.Y - _moveLastPoint.Value.Y,
            layer.Rect.W, layer.Rect.H);
        Layers.Elements[idx] = layer with { Rect = newRect };
        _moveLastPoint = point;
        PublishChange();
        return true;
    }

    public bool EndMove()
    {
        var moveId = _moveId;
        var fromRect = _moveFromRect;
        _moveId = null;
        _moveLastPoint = null;
        _moveFromRect = null;

        if (moveId == null || fromRect == null) return false;
        if (Layers.FirstIndex(moveId.Value) is not int idx) return false;

        var toRect = Layers.Elements[idx].Rect;
        if (toRect == fromRect) return false;

        History.Record(new AnnotationHistory.Mutation.Move(
            moveId.Value,
            fromRect,
            toRect,
            null,
            null));
        PublishChange();
        return true;
    }

    /// <summary>选区整体平移时，所有标注一起平移。</summary>
    public void TranslateAll(PointF delta)
    {
        if (Layers.IsEmpty) return;
        for (int i = 0; i < Layers.Elements.Count; i++)
        {
            var layer = Layers.Elements[i];
            Layers.Elements[i] = layer with { Rect = layer.Rect.OffsetBy(delta.X, delta.Y) };
        }
        PublishChange();
    }

    /// <summary>删除选中的图层。</summary>
    public bool DeleteSelected()
    {
        if (!SelectedId.HasValue) return false;
        var id = SelectedId.Value;
        SelectedId = null;
        if (Layers.FirstIndex(id) is not int idx) return false;

        var layer = Layers.Elements[idx];
        History.Record(new AnnotationHistory.Mutation.Remove(layer, idx, PixelatePreviews.GetValueOrDefault(id)));
        Layers.Remove(id);
        PixelatePreviews.Remove(id);
        PublishChange();
        return true;
    }

    /// <summary>把当前颜色应用到选中的图层。</summary>
    public bool ApplyColorToSelection()
    {
        return ApplyStyleToSelection(ToolStyleAxis.Color);
    }

    /// <summary>把当前样式轴的值应用到选中图层，并作为一次可撤销的样式修改记录。</summary>
    public bool ApplyStyleToSelection(ToolStyleAxis axis)
    {
        if (!SelectedId.HasValue) return false;
        if (Layers.FirstIndex(SelectedId.Value) is not int idx) return false;

        var before = Layers.Elements[idx];
        var after = axis switch
        {
            ToolStyleAxis.Color => before with
            {
                Color = before.Kind == LayerKind.Highlight
                    ? Color.WithAlpha(CurrentStyle.ValueFor(ToolStyleAxis.Opacity))
                    : Color
            },
            ToolStyleAxis.Width => before with
            {
                LineWidth = CurrentStyle.ValueFor(ToolStyleAxis.Width) * StrokeScale
            },
            ToolStyleAxis.FontSize => before with
            {
                FontSize = CurrentStyle.ValueFor(ToolStyleAxis.FontSize) * StrokeScale
            },
            ToolStyleAxis.Opacity => before with
            {
                Color = before.Color.WithAlpha(CurrentStyle.ValueFor(ToolStyleAxis.Opacity))
            },
            // 马赛克预览依赖原始像素，和 macOS 一样只影响之后新建的图层。
            ToolStyleAxis.BlockSize => before,
            ToolStyleAxis.Dim => before with
            {
                Dim = CurrentStyle.ValueFor(ToolStyleAxis.Dim)
            },
            _ => before
        };

        if (after == before) return false;

        Layers.Elements[idx] = after;
        History.RecordStyle(before.Id, before, after);
        PublishChange();
        return true;
    }

    // MARK: - 缩放

    public void BeginResize(Guid id, ResizeHandle handle, PointF point)
    {
        ResizeId = id;
        ActiveResizeHandle = handle;
        if (Layers.FirstIndex(id) is int idx)
            _resizeOriginal = Layers.Elements[idx];
        _resizeStartPoint = point;
        PublishChange();
    }

    public bool UpdateResize(PointF point)
    {
        if (ResizeId == null || ActiveResizeHandle == null || _resizeOriginal == null || _resizeStartPoint == null)
            return false;

        var delta = point - _resizeStartPoint.Value;
        var original = _resizeOriginal!;
        var descriptor = ToolRegistry.DescriptorFor(original.Kind);
        if (descriptor is null) return false;
        var resized = descriptor.Resize(original, ActiveResizeHandle.Value, delta, PixelScale);
        if (Layers.FirstIndex(ResizeId.Value) is int idx)
            Layers.Elements[idx] = resized;
        PublishChange();
        return true;
    }

    public bool EndResize()
    {
        if (ResizeId == null || _resizeOriginal == null)
        {
            ResizeId = null;
            ActiveResizeHandle = null;
            _resizeOriginal = null;
            _resizeStartPoint = null;
            return false;
        }

        var id = ResizeId.Value;
        var original = _resizeOriginal!;
        if (Layers.FirstIndex(id) is int idx)
        {
            var current = Layers.Elements[idx];
            if (current.Rect != original.Rect)
            {
                History.Record(new AnnotationHistory.Mutation.Move(id, original.Rect, current.Rect, null, null));
            }
        }
        ResizeId = null;
        ActiveResizeHandle = null;
        _resizeOriginal = null;
        _resizeStartPoint = null;
        PublishChange();
        return true;
    }

    // MARK: - 文字编辑

    /// <summary>
    /// Updates the contents of an existing text layer. Consecutive changes to the
    /// same layer are coalesced by <see cref="AnnotationHistory"/>, so typing in a
    /// UI editor remains a single undoable operation. Non-text and missing layer
    /// identifiers are rejected without mutating state.
    /// </summary>
    public bool SetText(Guid id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Layers.FirstIndex(id) is not int idx) return false;

        var before = Layers.Elements[idx];
        if (before.Kind != LayerKind.Text || string.Equals(before.Text, text, StringComparison.Ordinal))
            return false;

        var after = before with { Text = text };
        Layers.Elements[idx] = after;
        if (EditingTextId != id)
            History.RecordStyle(id, before, after);
        PublishChange();
        return true;
    }

    public void EndTextEditing()
    {
        if (EditingTextId is not { } editingId) return;
        EditingTextId = null;
        MarkedText = null;
        if (Layers.FirstIndex(editingId) is int index)
        {
            var draft = Layers.Elements[index];
            if (string.IsNullOrWhiteSpace(draft.Text))
            {
                Layers.Remove(editingId);
                if (SelectedId == editingId)
                    SelectedId = null;
            }
            else
                History.Record(new AnnotationHistory.Mutation.Add(draft, null));
        }
        PublishChange();
    }

    // MARK: - 撤销 / 重做

    public bool Undo()
    {
        bool discardedEmptyTextDraft = EditingTextId is { } editingId
            && Layers.FirstIndex(editingId) is int textIndex
            && string.IsNullOrWhiteSpace(Layers.Elements[textIndex].Text);
        EndTextEditing();
        if (discardedEmptyTextDraft)
            return true;
        if (History.PopUndo() is not { } mutation) return false;
        Revert(mutation);
        History.PushRedo(mutation);
        PublishChange();
        return true;
    }

    public bool Redo()
    {
        if (EditingTextId.HasValue) return false;
        if (History.PopRedo() is not { } mutation) return false;
        Apply(mutation);
        History.PushUndo(mutation);
        PublishChange();
        return true;
    }

    private void Revert(AnnotationHistory.Mutation mutation)
    {
        switch (mutation)
        {
            case AnnotationHistory.Mutation.Add add:
                Layers.Remove(add.Layer.Id);
                PixelatePreviews.Remove(add.Layer.Id);
                if (SelectedId == add.Layer.Id) SelectedId = null;
                break;

            case AnnotationHistory.Mutation.Remove remove:
                Layers.Insert(remove.Layer, remove.Index);
                if (remove.Preview != null)
                    PixelatePreviews[remove.Layer.Id] = remove.Preview;
                SelectedId = remove.Layer.Id;
                break;

            case AnnotationHistory.Mutation.Move move:
                if (Layers.FirstIndex(move.Id) is int idx)
                    Layers.Elements[idx] = Layers.Elements[idx] with { Rect = move.From };
                SelectedId = move.Id;
                break;

            case AnnotationHistory.Mutation.Style style:
                if (Layers.FirstIndex(style.Id) is int sIdx)
                    Layers.Elements[sIdx] = style.Before;
                break;
        }
    }

    private void Apply(AnnotationHistory.Mutation mutation)
    {
        switch (mutation)
        {
            case AnnotationHistory.Mutation.Add add:
                Layers.Append(add.Layer);
                if (add.Preview != null)
                    PixelatePreviews[add.Layer.Id] = add.Preview;
                break;

            case AnnotationHistory.Mutation.Remove remove:
                Layers.Remove(remove.Layer.Id);
                PixelatePreviews.Remove(remove.Layer.Id);
                if (SelectedId == remove.Layer.Id) SelectedId = null;
                break;

            case AnnotationHistory.Mutation.Move move:
                if (Layers.FirstIndex(move.Id) is int idx)
                    Layers.Elements[idx] = Layers.Elements[idx] with { Rect = move.To };
                SelectedId = move.Id;
                break;

            case AnnotationHistory.Mutation.Style style:
                if (Layers.FirstIndex(style.Id) is int sIdx)
                    Layers.Elements[sIdx] = style.After;
                break;
        }
    }

    // MARK: - 导出

    /// <summary>换算到图像像素空间。</summary>
    public Layers<ImageSpace> ExportLayers(LRect selection, double scale)
        => Layers.Projected(selection, scale);

    public Layers<ImageSpace> ExportLayers(LRect selection, double scaleX, double scaleY)
        => Layers.Projected(selection, scaleX, scaleY);
}
