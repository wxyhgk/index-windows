using Index.Platform.Clipboard;

namespace Index.Clipboard;

/// <summary>Filtering, selection and commands shared by the popup UI and future tests.</summary>
public sealed class ClipboardPopupViewModel
{
    private readonly List<ClipboardHistoryItem> _items = new();
    private IReadOnlyList<ClipboardHistoryItem> _visibleItems = Array.Empty<ClipboardHistoryItem>();
    private long? _selectedId;

    public string Query { get; private set; } = string.Empty;
    public ClipboardItemKind? KindFilter { get; private set; }
    public int SelectedIndex { get; private set; }

    public IReadOnlyList<ClipboardHistoryItem> VisibleItems => _visibleItems;

    public ClipboardHistoryItem? SelectedItem
    {
        get
        {
            return _visibleItems.Count == 0
                ? null
                : _visibleItems[Math.Clamp(SelectedIndex, 0, _visibleItems.Count - 1)];
        }
    }

    public void ReplaceItems(IEnumerable<ClipboardHistoryItem> items)
    {
        _items.Clear();
        _items.AddRange(items.OrderByDescending(item => item.CapturedAt));
        RebuildVisibleItems(preserveSelection: true);
    }

    public void SetQuery(string? query)
    {
        Query = query ?? string.Empty;
        RebuildVisibleItems(preserveSelection: true);
    }

    public void SetKindFilter(ClipboardItemKind? kind)
    {
        KindFilter = kind;
        RebuildVisibleItems(preserveSelection: true);
    }

    public void Select(long id)
    {
        var index = FindVisibleIndex(id);
        if (index >= 0)
        {
            SelectedIndex = index;
            _selectedId = id;
        }
    }

    public void MoveSelection(int delta)
    {
        var count = _visibleItems.Count;
        if (count == 0) return;
        SelectedIndex = ((SelectedIndex + delta) % count + count) % count;
        _selectedId = _visibleItems[SelectedIndex].Id;
    }

    public bool CopySelected(IClipboardWriter clipboard)
    {
        var text = SelectedItem?.Text;
        if (string.IsNullOrEmpty(text)) return false;
        clipboard.WriteText(text);
        return true;
    }

    public void TogglePinned(long id)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index >= 0)
        {
            _items[index] = _items[index] with { IsPinned = !_items[index].IsPinned };
            RebuildVisibleItems(preserveSelection: true);
        }
    }

    public void Delete(long id)
    {
        _items.RemoveAll(item => item.Id == id);
        RebuildVisibleItems(preserveSelection: true);
    }

    private void RebuildVisibleItems(bool preserveSelection)
    {
        var previousIndex = SelectedIndex;
        var previousId = preserveSelection ? _selectedId ?? SelectedItem?.Id : null;
        IEnumerable<ClipboardHistoryItem> result = _items;
        if (KindFilter is { } kind)
            result = result.Where(item => item.Kind == kind);

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var query = Query.Trim();
            result = result.Where(item =>
                item.ResolvedDisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (item.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (item.SourceApplication?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || (item.FilePaths?.Any(path => path.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ?? false));
        }

        _visibleItems = result.ToArray();
        var preservedIndex = previousId is { } id ? FindVisibleIndex(id) : -1;
        SelectedIndex = preservedIndex >= 0
            ? preservedIndex
            : Math.Clamp(previousIndex, 0, Math.Max(0, _visibleItems.Count - 1));
        _selectedId = _visibleItems.Count == 0 ? null : _visibleItems[SelectedIndex].Id;
    }

    private int FindVisibleIndex(long id)
    {
        for (var index = 0; index < _visibleItems.Count; index++)
        {
            if (_visibleItems[index].Id == id)
                return index;
        }
        return -1;
    }
}
