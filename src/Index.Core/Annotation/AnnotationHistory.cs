namespace Index.Annotation;

/// <summary>
/// 标注撤销/重做操作栈。纯数据 + 出入栈规则，不依赖图层存储。
/// 对应 macOS 端 AnnotationHistory。
/// </summary>
public sealed class AnnotationHistory
{
    /// <summary>一次可逆的改动。</summary>
    public abstract class Mutation
    {
        public sealed class Add : Mutation
        {
            public Layer Layer { get; }
            public byte[]? Preview { get; }
            public Add(Layer layer, byte[]? preview) { Layer = layer; Preview = preview; }
        }

        public sealed class Remove : Mutation
        {
            public Layer Layer { get; }
            public int Index { get; }
            public byte[]? Preview { get; }
            public Remove(Layer layer, int index, byte[]? preview) { Layer = layer; Index = index; Preview = preview; }
        }

        public sealed class Move : Mutation
        {
            public Guid Id { get; }
            public LRect From { get; }
            public LRect To { get; }
            public byte[]? OldPreview { get; }
            public byte[]? NewPreview { get; }
            public Move(Guid id, LRect from, LRect to, byte[]? oldPreview, byte[]? newPreview)
            { Id = id; From = from; To = to; OldPreview = oldPreview; NewPreview = newPreview; }
        }

        public sealed class Style : Mutation
        {
            public Guid Id { get; }
            public Layer Before { get; }
            public Layer After { get; }
            public Style(Guid id, Layer before, Layer after) { Id = id; Before = before; After = after; }
        }
    }

    private readonly List<Mutation> _undoStack = new();
    private readonly List<Mutation> _redoStack = new();
    private Guid? _coalescingTextId;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>所有写路径的唯一入口：新动作发生后，之前撤销掉的分支就不可再重做。</summary>
    public void Record(Mutation mutation)
    {
        _coalescingTextId = null;
        _undoStack.Add(mutation);
        _redoStack.Clear();
    }

    /// <summary>
    /// 记一条 style 改动；针对同一层的连续 style 合并成一条记录。
    /// </summary>
    public void RecordStyle(Guid id, Layer before, Layer after)
    {
        if (_coalescingTextId == id && _undoStack.Count > 0
            && _undoStack[^1] is Mutation.Style last && last.Id == id)
        {
            _undoStack[^1] = new Mutation.Style(id, last.Before, after);
            return;
        }
        Record(new Mutation.Style(id, before, after));
        _coalescingTextId = id;
    }

    public Mutation? PopUndo()
    {
        if (_undoStack.Count == 0) return null;
        var mutation = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _coalescingTextId = null;
        return mutation;
    }

    public void PushRedo(Mutation mutation) => _redoStack.Add(mutation);

    public Mutation? PopRedo()
    {
        if (_redoStack.Count == 0) return null;
        var mutation = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _coalescingTextId = null;
        return mutation;
    }

    public void PushUndo(Mutation mutation) => _undoStack.Add(mutation);

    public void BreakCoalescing() => _coalescingTextId = null;

    public void Reset()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _coalescingTextId = null;
    }
}
