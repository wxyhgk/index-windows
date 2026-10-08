using Index.Editor;
using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Editor;

/// <summary>Revision picker presentation. The workspace owns save-before-restore.</summary>
internal sealed class EditorRevisionHistoryView : UserControl
{
    private readonly GalleryTheme _theme;
    private readonly ListView _items = new();
    private readonly TextBlock _empty = new();
    private bool _updating;
    private string _historySignature = string.Empty;

    public EditorRevisionHistoryView(GalleryTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _items.SelectionMode = ListViewSelectionMode.Single;
        _items.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _items.SelectionChanged += OnSelectionChanged;
        _empty.Text = "保存标注后会在这里生成版本";
        _empty.FontSize = 11;
        _empty.Foreground = _theme.Muted;
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.Margin = new Thickness(4, 4, 4, 8);
        Content = BuildLayout();
    }

    public event Action<long>? RevisionRequested;

    public void Render(
        IReadOnlyList<ShotEditorHistoryItem> history,
        long? currentRevisionId)
    {
        ArgumentNullException.ThrowIfNull(history);
        _updating = true;
        try
        {
            string signature = string.Join(
                '|',
                history.Select(item =>
                    $"{item.RevisionId}:{item.CreatedAt.UtcTicks}:{item.LayerCount}:{item.Note}"));
            if (!string.Equals(signature, _historySignature, StringComparison.Ordinal))
            {
                _historySignature = signature;
                _items.Items.Clear();
                int version = history.Count;
                foreach (var item in history.Reverse())
                {
                    var row = new StackPanel { Spacing = 2 };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"版本 {version--}",
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = _theme.Text
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{item.CreatedAt.ToLocalTime():MM-dd HH:mm:ss} · {item.LayerCount} 个图层",
                        FontSize = 10,
                        Foreground = _theme.Muted
                    });
                    _items.Items.Add(new ListViewItem
                    {
                        Tag = item.RevisionId,
                        Padding = new Thickness(6),
                        Content = row
                    });
                }
            }

            _items.SelectedItem = _items.Items
                .OfType<ListViewItem>()
                .FirstOrDefault(item => item.Tag is long id && id == currentRevisionId);
            _empty.Visibility = history.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            _updating = false;
        }
    }

    private FrameworkElement BuildLayout()
    {
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(new TextBlock
        {
            Text = "版本历史",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            Margin = new Thickness(4, 2, 4, 4)
        });
        Grid.SetRow(_empty, 1);
        body.Children.Add(_empty);
        Grid.SetRow(_items, 2);
        body.Children.Add(_items);
        return new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = body
        };
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updating)
            return;
        if (_items.SelectedItem is ListViewItem { Tag: long revisionId })
            RevisionRequested?.Invoke(revisionId);
    }
}
