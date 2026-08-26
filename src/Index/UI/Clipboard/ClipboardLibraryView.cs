using Index.Clipboard;
using Index.Platform.Clipboard;
using Index.UI.Gallery;
using Index.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Clipboard;

/// <summary>Embedded clipboard-history page for the main library window.</summary>
internal sealed class ClipboardLibraryView : UserControl, IDisposable
{
    private const int InitialResultLimit = 60;
    private const int RenderBatchSize = 12;
    private readonly IClipboardHistorySource _source;
    private readonly IClipboardHistoryStore? _store;
    private readonly IClipboardWriter _writer;
    private readonly GalleryTheme _theme;
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel _detail = new() { Spacing = 12 };
    private readonly TextBlock _count = new();
    private readonly TextBlock _status = new();
    private readonly SearchInputBox _search = new()
    {
        PlaceholderText = "搜索剪贴板历史",
        Width = 210,
        Height = 36,
        CornerRadius = new CornerRadius(18)
    };
    private readonly Button _copy = new() { Content = "复制", IsEnabled = false };
    private readonly Button _pin = new() { Content = "固定", IsEnabled = false };
    private readonly Button _delete = new() { Content = "删除", IsEnabled = false };
    private readonly StackPanel _actions = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8
    };
    private IReadOnlyList<ClipboardHistoryItem> _items = [];
    private CancellationTokenSource? _loadCancellation;
    private int _generation;
    private bool _disposed;

    public ClipboardLibraryView(
        IClipboardHistorySource source,
        IClipboardWriter writer,
        GalleryTheme theme)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = source as IClipboardHistoryStore;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        _copy.Click += CopyClicked;
        _pin.Click += PinClicked;
        _delete.Click += DeleteClicked;
        _actions.Children.Add(_copy);
        _actions.Children.Add(_pin);
        _actions.Children.Add(_delete);

        Content = BuildContent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private FrameworkElement BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 16, 0, 14), ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock
        {
            Text = "剪贴板历史",
            FontSize = 21,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text
        });
        _count.FontSize = 12;
        _count.Foreground = _theme.Muted;
        heading.Children.Add(_count);
        header.Children.Add(heading);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        filters.Children.Add(MakeFilterChip("全部", selected: true));
        filters.Children.Add(MakeFilterChip("文本"));
        filters.Children.Add(MakeFilterChip("图片"));
        filters.Children.Add(MakeFilterChip("文件"));
        Grid.SetColumn(filters, 1);
        header.Children.Add(filters);
        _search.Background = _theme.Selected;
        _search.Foreground = _theme.Text;
        _search.BorderBrush = _theme.CardBorder;
        Grid.SetColumn(_search, 2);
        header.Children.Add(_search);
        var refresh = MakeButton("刷新");
        refresh.Click += async (_, _) => await ReloadAsync();
        Grid.SetColumn(refresh, 3);
        header.Children.Add(refresh);
        root.Children.Add(header);

        var body = new Grid { ColumnSpacing = 16 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MaxWidth = 390 });

        _list.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _list.BorderThickness = new Thickness(0);
        _list.SelectionChanged += (_, _) => ShowSelection();
        body.Children.Add(_list);

        var detailBorder = new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Child = _detail
        };
        Grid.SetColumn(detailBorder, 1);
        body.Children.Add(detailBorder);
        ShowSelection();

        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_items.Count == 0)
            _ = ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
        => CancelLoad();

    private async Task ReloadAsync()
    {
        CancelLoad();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var generation = ++_generation;
        SetStatus("正在读取剪贴板历史…");
        try
        {
            var query = _search.Text.Trim();
            var items = await _source.SearchAsync(
                query,
                kind: null,
                InitialResultLimit,
                cancellation.Token);
            if (cancellation.IsCancellationRequested || generation != _generation || _disposed)
                return;
            _items = items;
            await ApplyItemsAsync(generation, cancellation.Token);
            SetStatus(items.Count == 0 ? "还没有剪贴板记录" : "");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (generation == _generation && !_disposed)
                SetStatus($"剪贴板历史读取失败：{error.Message}");
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
                _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ApplyItemsAsync(int generation, CancellationToken cancellationToken)
    {
        var query = _search.Text.Trim();
        var previousId = SelectedItem?.Id;
        _list.Items.Clear();
        for (var index = 0; index < _items.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || generation != _generation)
                return;
            var item = _items[index];
            _list.Items.Add(BuildRow(item));
            if ((index + 1) % RenderBatchSize == 0)
                await Task.Yield();
        }
        _count.Text = string.IsNullOrEmpty(query)
            ? $"最近 {_items.Count:N0} 个项目"
            : $"{_items.Count:N0} 个匹配项目";
        if (previousId is not null)
        {
            _list.SelectedItem = _list.Items
                .OfType<FrameworkElement>()
                .FirstOrDefault(element => (element.Tag as ClipboardHistoryItem)?.Id == previousId);
        }
        if (_list.SelectedItem is null && _list.Items.Count > 0)
            _list.SelectedIndex = 0;
        ShowSelection();
    }

    private FrameworkElement BuildRow(ClipboardHistoryItem item)
    {
        var row = new Grid { Padding = new Thickness(4), ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Background = _theme.Selected,
            Child = new FontIcon
            {
                Glyph = KindGlyph(item.Kind),
                FontSize = 17,
                Foreground = _theme.Text
            }
        });
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = item.ResolvedDisplayName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        labels.Children.Add(new TextBlock
        {
            Text = item.Summary,
            FontSize = 12,
            Foreground = _theme.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        var metadata = new TextBlock
        {
            Text = $"{(item.IsPinned ? "已固定  ·  " : "")}{RelativeTime(item.CapturedAt)}",
            FontSize = 11,
            Foreground = _theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(metadata, 2);
        row.Children.Add(metadata);
        return new Border
        {
            Tag = item,
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 6, 8),
            Child = row
        };
    }

    private ClipboardHistoryItem? SelectedItem
        => (_list.SelectedItem as FrameworkElement)?.Tag as ClipboardHistoryItem;

    private void ShowSelection()
    {
        _detail.Children.Clear();
        var item = SelectedItem;
        _copy.IsEnabled = item is not null;
        _pin.IsEnabled = item is not null && _store is not null;
        _delete.IsEnabled = item is not null && _store is not null;
        if (item is null)
        {
            _detail.Children.Add(new TextBlock
            {
                Text = "选择一条记录查看详情",
                Foreground = _theme.Muted,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        _detail.Children.Add(new TextBlock
        {
            Text = item.ResolvedDisplayName,
            FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            TextWrapping = TextWrapping.Wrap
        });
        _detail.Children.Add(new TextBlock
        {
            Text = $"{KindName(item.Kind)} · {item.SourceApplication ?? "未知来源"} · {item.CapturedAt.LocalDateTime:g}",
            FontSize = 12,
            Foreground = _theme.Muted,
            TextWrapping = TextWrapping.Wrap
        });
        _detail.Children.Add(new TextBlock
        {
            Text = item.Text ?? item.Summary,
            FontSize = 13,
            Foreground = _theme.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 260
        });

        _copy.Content = "复制";
        _pin.Content = item.IsPinned ? "取消固定" : "固定";
        _detail.Children.Add(_actions);
        _status.Foreground = _theme.Muted;
        _status.FontSize = 12;
        _status.TextWrapping = TextWrapping.Wrap;
        _detail.Children.Add(_status);
    }

    private async void CopyClicked(object sender, RoutedEventArgs args)
    {
        if (SelectedItem is not { } item) return;
        try
        {
            if (item.Kind == ClipboardItemKind.Text && item.Text is not null)
                _writer.WriteText(item.Text);
            else if (item.Kind == ClipboardItemKind.Image && item.AssetPath is not null)
                await _writer.WritePngAsync(await File.ReadAllBytesAsync(item.AssetPath));
            else if (item.Kind == ClipboardItemKind.File && item.FilePaths is { Count: > 0 })
                await _writer.WriteFilesAsync(item.FilePaths);
            else
                throw new InvalidOperationException("这条记录没有可复制的内容。");
            SetStatus("已复制到剪贴板");
        }
        catch (Exception error)
        {
            SetStatus($"复制失败：{error.Message}");
        }
    }

    private async void PinClicked(object sender, RoutedEventArgs args)
    {
        if (_store is null || SelectedItem is not { } item) return;
        await MutateAsync(token => _store.TogglePinnedAsync(item.Id, token), "固定状态更新失败");
    }

    private async void DeleteClicked(object sender, RoutedEventArgs args)
    {
        if (_store is null || SelectedItem is not { } item) return;
        await MutateAsync(token => _store.DeleteAsync(item.Id, token), "删除失败");
    }

    private async Task MutateAsync(Func<CancellationToken, Task> action, string failurePrefix)
    {
        try
        {
            await action(CancellationToken.None);
            await ReloadAsync();
        }
        catch (Exception error)
        {
            SetStatus($"{failurePrefix}：{error.Message}");
        }
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        if (SelectedItem is null && !string.IsNullOrEmpty(message))
        {
            _detail.Children.Clear();
            _detail.Children.Add(_status);
        }
    }

    private Button MakeButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(14, 7, 14, 7),
        CornerRadius = new CornerRadius(16),
        Background = _theme.Selected,
        Foreground = _theme.Text,
        BorderThickness = new Thickness(0)
    };

    private Button MakeFilterChip(string text, bool selected = false) => new()
    {
        Content = text,
        Height = 36,
        MinWidth = 52,
        Padding = new Thickness(12, 0, 12, 0),
        CornerRadius = new CornerRadius(18),
        Background = selected ? _theme.Selected : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        Foreground = selected ? _theme.Text : _theme.Muted,
        BorderThickness = new Thickness(0),
        IsHitTestVisible = false
    };

    private void StyleActionButton(Button button)
    {
        button.Padding = new Thickness(16, 7, 16, 7);
        button.CornerRadius = new CornerRadius(16);
        button.Background = _theme.Selected;
        button.Foreground = _theme.Text;
        button.BorderThickness = new Thickness(0);
    }

    private void CancelLoad()
    {
        _generation++;
        _loadCancellation?.Cancel();
        _loadCancellation = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLoad();
        _copy.Click -= CopyClicked;
        _pin.Click -= PinClicked;
        _delete.Click -= DeleteClicked;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private static string KindName(ClipboardItemKind kind) => kind switch
    {
        ClipboardItemKind.Text => "文本",
        ClipboardItemKind.Image => "图片",
        _ => "文件"
    };

    private static string KindGlyph(ClipboardItemKind kind) => kind switch
    {
        ClipboardItemKind.Text => "\uE8A5",
        ClipboardItemKind.Image => "\uEB9F",
        _ => "\uE8B7"
    };

    private static string RelativeTime(DateTimeOffset capturedAt)
    {
        var elapsed = DateTimeOffset.Now - capturedAt;
        if (elapsed.TotalMinutes < 1) return "刚刚";
        if (elapsed.TotalHours < 1) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed.TotalDays < 1) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed.TotalDays < 7) return $"{Math.Max(1, (int)elapsed.TotalDays)} 天前";
        return capturedAt.LocalDateTime.ToString("yyyy-MM-dd");
    }
}
