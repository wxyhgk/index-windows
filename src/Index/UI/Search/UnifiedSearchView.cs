using Index.Clipboard;
using Index.Platform.Clipboard;
using Index.Recognition;
using Index.Search;
using Index.Storage;
using Index.UI.Controls;
using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Index.UI.Search;

/// <summary>截图与剪贴板的统一搜索工作区。</summary>
internal sealed class UnifiedSearchView : UserControl, IDisposable
{
    private readonly IUnifiedSearchService _searchService;
    private readonly IShotAssetReader _shotAssets;
    private readonly IClipboardWriter _clipboard;
    private readonly GalleryTheme _theme;
    private readonly SearchInputBox _query = new()
    {
        PlaceholderText = "搜索截图、窗口、网址、标签或剪贴板内容",
        MinWidth = 420
    };
    private readonly Button _searchButton = new() { Content = "搜索" };
    private readonly ListView _results = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel _preview = new() { Spacing = 12 };
    private readonly TextBlock _status = new();
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _previewCancellation;
    private int _generation;
    private bool _disposed;

    public UnifiedSearchView(
        IUnifiedSearchService searchService,
        IShotAssetReader shotAssets,
        IClipboardWriter clipboard,
        GalleryTheme theme)
    {
        _searchService = searchService;
        _shotAssets = shotAssets;
        _clipboard = clipboard;
        _theme = theme;
        Content = BuildContent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private FrameworkElement BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 16, 0, 14), ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "统一搜索",
            FontSize = 21,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            VerticalAlignment = VerticalAlignment.Center
        });
        _query.KeyDown += QueryKeyDown;
        Grid.SetColumn(_query, 1);
        header.Children.Add(_query);
        _searchButton.Click += SearchClicked;
        Grid.SetColumn(_searchButton, 2);
        header.Children.Add(_searchButton);
        root.Children.Add(header);
        var body = new Grid { ColumnSpacing = 16 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MaxWidth = 420 });
        _results.SelectionChanged += (_, _) => _ = ShowSelectionAsync();
        body.Children.Add(_results);
        var previewBorder = new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Child = _preview
        };
        Grid.SetColumn(previewBorder, 1);
        body.Children.Add(previewBorder);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        SetStatus("输入关键词后按回车或点击搜索");
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        CancelSearch();
        CancelPreview();
    }

    private void QueryKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Enter)
            return;
        args.Handled = true;
        _ = SearchAsync();
    }

    private void SearchClicked(object sender, RoutedEventArgs args)
        => _ = SearchAsync();

    private async Task SearchAsync()
    {
        CancelSearch();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var generation = ++_generation;
        _searchButton.IsEnabled = false;
        try
        {
            SetStatus("正在搜索…");
            var entries = await _searchService.SearchAsync(_query.Text, 30, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || generation != _generation)
                return;

            var previousId = SelectedEntry?.Id;
            _results.Items.Clear();
            foreach (var entry in entries)
                _results.Items.Add(BuildRow(entry));
            if (previousId is not null)
            {
                _results.SelectedItem = _results.Items.OfType<FrameworkElement>()
                    .FirstOrDefault(element => (element.Tag as UnifiedSearchEntry)?.Id == previousId);
            }
            if (_results.SelectedItem is null && _results.Items.Count > 0)
                _results.SelectedIndex = 0;
            SetStatus(entries.Count == 0 ? "没有找到匹配内容" : $"{entries.Count} 个结果");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_disposed && generation == _generation)
                SetStatus($"搜索失败：{error.Message}");
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
                _searchCancellation = null;
            if (!_disposed && generation == _generation)
                _searchButton.IsEnabled = true;
            cancellation.Dispose();
        }
    }

    private FrameworkElement BuildRow(UnifiedSearchEntry entry)
    {
        var row = new Grid { Padding = new Thickness(10), ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon
        {
            Glyph = KindGlyph(entry.Kind),
            FontSize = 18,
            Foreground = _theme.Text,
            VerticalAlignment = VerticalAlignment.Center
        });
        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        labels.Children.Add(new TextBlock
        {
            Text = entry.Summary,
            FontSize = 12,
            Foreground = _theme.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        var source = new TextBlock
        {
            Text = entry.Subtitle,
            FontSize = 11,
            Foreground = _theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(source, 2);
        row.Children.Add(source);
        return new Border
        {
            Tag = entry,
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 6, 8),
            Child = row
        };
    }

    private UnifiedSearchEntry? SelectedEntry
        => (_results.SelectedItem as FrameworkElement)?.Tag as UnifiedSearchEntry;

    private async Task ShowSelectionAsync()
    {
        CancelPreview();
        _preview.Children.Clear();
        if (SelectedEntry is not { } entry)
        {
            SetStatus("输入关键词搜索截图和剪贴板");
            return;
        }

        _preview.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            TextWrapping = TextWrapping.Wrap
        });
        _preview.Children.Add(new TextBlock
        {
            Text = $"{KindName(entry.Kind)} · {entry.Timestamp.LocalDateTime:g}",
            FontSize = 12,
            Foreground = _theme.Muted
        });

        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        try
        {
            var imageBytes = await ReadImageAsync(entry, cancellation.Token);
            if (imageBytes is not null && ReferenceEquals(SelectedEntry, entry))
            {
                var bitmap = new BitmapImage { DecodePixelHeight = 600 };
                using var stream = new MemoryStream(imageBytes);
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                _preview.Children.Add(new Image
                {
                    Source = bitmap,
                    MaxHeight = 300,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                });
            }
            else if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                _preview.Children.Add(new TextBlock
                {
                    Text = entry.Summary,
                    FontSize = 13,
                    Foreground = _theme.Text,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 300
                });
            }
            var copy = new Button { Content = "复制", HorizontalAlignment = HorizontalAlignment.Left };
            copy.Click += async (_, _) => await CopyAsync(entry);
            _preview.Children.Add(copy);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            SetStatus($"预览失败：{error.Message}");
        }
    }

    private async Task<byte[]?> ReadImageAsync(
        UnifiedSearchEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Shot is { } shot)
            return (await _shotAssets.ReadPreviewAsync(shot, cancellationToken)).Data.ToArray();
        if (entry.ClipboardItem is { Kind: ClipboardItemKind.Image, AssetPath: { } path })
            return await File.ReadAllBytesAsync(path, cancellationToken);
        return null;
    }

    private async Task CopyAsync(UnifiedSearchEntry entry)
    {
        try
        {
            if (entry.Shot is { } shot)
            {
                var asset = await _shotAssets.ReadBestAvailableAsync(shot);
                await _clipboard.WritePngAsync(asset.Data);
            }
            else if (entry.ClipboardItem is { } item)
            {
                if (item.Kind == ClipboardItemKind.Text && item.Text is not null)
                    _clipboard.WriteText(item.Text);
                else if (item.Kind == ClipboardItemKind.Image && item.AssetPath is not null)
                    await _clipboard.WritePngAsync(await File.ReadAllBytesAsync(item.AssetPath));
                else if (item.Kind == ClipboardItemKind.File && item.FilePaths is { Count: > 0 })
                    await _clipboard.WriteFilesAsync(item.FilePaths);
            }
            SetStatus("已复制到剪贴板");
        }
        catch (Exception error)
        {
            SetStatus($"复制失败：{error.Message}");
        }
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.Foreground = _theme.Muted;
        _status.FontSize = 12;
        if (!_preview.Children.Contains(_status))
            _preview.Children.Add(_status);
    }

    private void CancelSearch()
    {
        _generation++;
        _searchCancellation?.Cancel();
        _searchCancellation = null;
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelSearch();
        CancelPreview();
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _query.KeyDown -= QueryKeyDown;
        _searchButton.Click -= SearchClicked;
    }

    private static string KindName(UnifiedSearchEntryKind kind) => kind switch
    {
        UnifiedSearchEntryKind.Screenshot => "截图",
        UnifiedSearchEntryKind.ClipboardText => "剪贴板文本",
        UnifiedSearchEntryKind.ClipboardImage => "剪贴板图片",
        _ => "剪贴板文件"
    };

    private static string KindGlyph(UnifiedSearchEntryKind kind) => kind switch
    {
        UnifiedSearchEntryKind.Screenshot => "\uEB9F",
        UnifiedSearchEntryKind.ClipboardText => "\uE8A5",
        UnifiedSearchEntryKind.ClipboardImage => "\uE91B",
        _ => "\uE8B7"
    };
}
