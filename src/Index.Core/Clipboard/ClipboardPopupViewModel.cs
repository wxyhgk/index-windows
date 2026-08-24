using Index.Platform.Clipboard;

namespace Index.Clipboard;

/// <summary>Filtering, selection and commands shared by the popup UI and future tests.</summary>
public sealed class ClipboardPopupViewModel
{
    private readonly List<ClipboardHistoryItem> _items = new();

    public string Query { get; private set; } = string.Empty;
    public ClipboardItemKind? KindFilter { get; private set; }
    public int SelectedIndex { get; private set; }

    public IReadOnlyList<ClipboardHistoryItem> VisibleItems
    {
        get
        {
            IEnumerable<ClipboardHistoryItem> result = _items;
            if (KindFilter is { } kind)
                result = result.Where(item => item.Kind == kind);

            if (!string.IsNullOrWhiteSpace(Query))
            {
                var query = Query.Trim();
                result = result.Where(item =>
                    item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || item.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || (item.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    || (item.SourceApplication?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false));
            }
            return result.ToArray();
        }
    }

    public ClipboardHistoryItem? SelectedItem
    {
        get
        {
            var visible = VisibleItems;
            return visible.Count == 0 ? null : visible[Math.Clamp(SelectedIndex, 0, visible.Count - 1)];
        }
    }

    public void ReplaceItems(IEnumerable<ClipboardHistoryItem> items)
    {
        _items.Clear();
        _items.AddRange(items.OrderByDescending(item => item.CapturedAt));
        SelectedIndex = 0;
    }

    public void SetQuery(string? query)
    {
        Query = query ?? string.Empty;
        ClampSelection();
    }

    public void SetKindFilter(ClipboardItemKind? kind)
    {
        KindFilter = kind;
        SelectedIndex = 0;
    }

    public void Select(long id)
    {
        var visible = VisibleItems;
        var index = visible.Select((item, index) => (item, index))
            .FirstOrDefault(pair => pair.item.Id == id).index;
        if (visible.Count > 0 && visible[index].Id == id)
            SelectedIndex = index;
    }

    public void MoveSelection(int delta)
    {
        var count = VisibleItems.Count;
        if (count == 0) return;
        SelectedIndex = ((SelectedIndex + delta) % count + count) % count;
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
            _items[index] = _items[index] with { IsPinned = !_items[index].IsPinned };
    }

    public void Delete(long id)
    {
        _items.RemoveAll(item => item.Id == id);
        ClampSelection();
    }

    private void ClampSelection()
    {
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, VisibleItems.Count - 1));
    }
}
