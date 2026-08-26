using Index.Clipboard;
using Index.Platform.Clipboard;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Index.UI.Controls;
using Windows.System;

namespace Index.UI.Clipboard;

/// <summary>Stable second-window shell for clipboard history.</summary>
public sealed class ClipboardPopupWindow : Window
{
    private const double CardWidth = 136;
    private const double CardHeight = 150;
    private static readonly SolidColorBrush Panel = Brush(0xF4, 0xF7, 0xFB);
    private static readonly SolidColorBrush Card = Brush(0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush Border = Brush(0xD8, 0xE0, 0xEA);
    private static readonly SolidColorBrush Text = Brush(0x1F, 0x29, 0x37);
    private static readonly SolidColorBrush Muted = Brush(0x64, 0x74, 0x8B);
    private static readonly SolidColorBrush Accent = Brush(0x4F, 0x6F, 0xD8);

    private readonly ClipboardPopupViewModel _viewModel;
    private readonly IClipboardWriter _clipboard;
    private readonly Func<long, CancellationToken, Task>? _togglePinned;
    private readonly Func<long, CancellationToken, Task>? _delete;
    private readonly Func<CancellationToken, Task>? _paste;
    private readonly Action? _closeRequested;
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Dictionary<long, CardEntry> _cardCache = new();
    private readonly Dictionary<int, ToggleButton> _filterButtons = new();
    private ScrollViewer? _stableScroller;
    private SearchInputBox? _searchBox;
    private TextBlock? _emptyState;
    private bool _initialized;
    private bool _isWindowPinned;
    private bool _hasPositionedCards;
    private bool _updatingFilters;
    private int _activationVersion;

    public ClipboardPopupWindow(
        ClipboardPopupViewModel viewModel,
        IClipboardWriter clipboard,
        Func<long, CancellationToken, Task>? togglePinned = null,
        Func<long, CancellationToken, Task>? delete = null,
        Func<CancellationToken, Task>? paste = null,
        Action? closeRequested = null)
    {
        _viewModel = viewModel;
        _clipboard = clipboard;
        _togglePinned = togglePinned;
        _delete = delete;
        _paste = paste;
        _closeRequested = closeRequested;
        AppWindow.Title = "Index · 剪贴板";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 360));
        Activated += HandleWindowActivated;
        Content = new Grid
        {
            Background = Panel,
            Children =
            {
                new TextBlock
                {
                    Text = "正在加载剪贴板历史…",
                    Foreground = Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    public void InitializeContent()
    {
        if (_initialized) return;
        _initialized = true;
        var root = BuildStableRoot();
        root.KeyDown += HandleKeyDown;
        Content = root;
        SynchronizeCards();
        _searchBox?.Focus(FocusState.Programmatic);
    }

    public void ApplyWindowChrome() => ClipboardPopupChrome.ApplyStandard(this);
    public void Refresh() => SynchronizeCards();

    private Grid BuildStableRoot()
    {
        var root = new Grid { Background = Panel, Padding = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        heading.Children.Add(new TextBlock
        {
            Text = "剪贴板历史",
            Foreground = Text,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        heading.Children.Add(new TextBlock
        {
            Text = "← → 选择  ·  Enter 粘贴  ·  P 固定  ·  Delete 删除  ·  Esc 关闭",
            Foreground = Muted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(heading);

        var pinButton = BuildHeaderButton("\uE840", "钉住弹窗");
        pinButton.Click += (_, _) =>
        {
            _isWindowPinned = !_isWindowPinned;
            pinButton.Background = _isWindowPinned ? Accent : new SolidColorBrush(Colors.Transparent);
            pinButton.Foreground = _isWindowPinned ? new SolidColorBrush(Colors.White) : Muted;
            ToolTipService.SetToolTip(pinButton, _isWindowPinned ? "取消钉住" : "钉住弹窗");
        };
        Grid.SetColumn(pinButton, 1);
        header.Children.Add(pinButton);
        root.Children.Add(header);

        var controls = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 10) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _searchBox = new SearchInputBox
        {
            PlaceholderText = "搜索文本、来源应用或文件名",
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Text = _viewModel.Query
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _viewModel.SetQuery(_searchBox.Text);
            SynchronizeCards();
        };
        _searchBox.KeyDown += HandleSearchKeyDown;
        controls.Children.Add(_searchBox);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AddFilter(filters, null, "全部");
        AddFilter(filters, ClipboardItemKind.Text, "文本");
        AddFilter(filters, ClipboardItemKind.Image, "图片");
        AddFilter(filters, ClipboardItemKind.File, "文件");
        Grid.SetColumn(filters, 1);
        controls.Children.Add(filters);
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);

        var contentHost = new Grid();
        _stableScroller = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _cards
        };
        contentHost.Children.Add(_stableScroller);
        _emptyState = new TextBlock
        {
            Text = "没有匹配的剪贴板内容",
            Foreground = Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        contentHost.Children.Add(_emptyState);
        Grid.SetRow(contentHost, 2);
        root.Children.Add(contentHost);
        return root;
    }

    private void AddFilter(StackPanel host, ClipboardItemKind? kind, string label)
    {
        var button = new ToggleButton
        {
            Content = label,
            Height = 36,
            MinWidth = 52,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(8),
            IsChecked = _viewModel.KindFilter == kind
        };
        button.Click += (_, _) =>
        {
            if (_updatingFilters) return;
            _viewModel.SetKindFilter(kind);
            UpdateFilterButtons();
            SynchronizeCards();
        };
        _filterButtons[FilterKey(kind)] = button;
        host.Children.Add(button);
    }

    private void UpdateFilterButtons()
    {
        _updatingFilters = true;
        foreach (var pair in _filterButtons)
            pair.Value.IsChecked = pair.Key == FilterKey(_viewModel.KindFilter);
        _updatingFilters = false;
    }

    private static int FilterKey(ClipboardItemKind? kind) => kind is { } value ? (int)value : -1;

    private static Button BuildHeaderButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Foreground = Muted,
            Content = new FontIcon { Glyph = glyph, FontSize = 12 }
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private void HandleWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        int version = ++_activationVersion;
        if (e.WindowActivationState == WindowActivationState.Deactivated && !_isWindowPinned)
            _ = ConfirmCloseAfterDeactivationAsync(version);
    }

    private async Task ConfirmCloseAfterDeactivationAsync(int version)
    {
        await Task.Delay(180);
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (version == _activationVersion
                && !_isWindowPinned
                && !ClipboardPopupChrome.IsForegroundOrCapturing(this))
                RequestClose();
        });
    }

    private void RequestClose()
    {
        if (_closeRequested is not null)
            _closeRequested();
        else
            Close();
    }

    /// <summary>Reconciles controls in-place so searching and moving selection retain thumbnails.</summary>
    private void SynchronizeCards()
    {
        if (!_initialized) return;
        var items = _viewModel.VisibleItems;
        var desiredIds = new HashSet<long>(items.Select(item => item.Id));
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!_cardCache.TryGetValue(item.Id, out var entry))
            {
                entry = CreateCard(item);
                _cardCache[item.Id] = entry;
            }
            else if (entry.Item != item)
            {
                entry.Item = item;
                entry.Button.Tag = item;
                entry.Button.Content = BuildCardContent(item);
            }

            var currentIndex = _cards.Children.IndexOf(entry.Button);
            if (currentIndex == index) continue;
            if (currentIndex >= 0)
                _cards.Children.RemoveAt(currentIndex);
            _cards.Children.Insert(index, entry.Button);
        }
        for (var index = _cards.Children.Count - 1; index >= items.Count; index--)
            _cards.Children.RemoveAt(index);

        // Filtered cards remain cached. Only an unfiltered source refresh can prove an item is gone.
        if (_viewModel.Query.Length == 0 && _viewModel.KindFilter is null)
        {
            foreach (var staleId in _cardCache.Keys.Where(id => !desiredIds.Contains(id)).ToArray())
                _cardCache.Remove(staleId);
        }

        if (_emptyState is not null)
            _emptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_stableScroller is not null)
            _stableScroller.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateSelectionVisuals(bringIntoView: false);

        if (!_hasPositionedCards && items.Count > 0)
        {
            _hasPositionedCards = true;
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _stableScroller?.ChangeView(0, null, null, true));
        }
    }

    private CardEntry CreateCard(ClipboardHistoryItem item)
    {
        var button = new Button
        {
            Tag = item,
            Width = CardWidth,
            MinWidth = CardWidth,
            MaxWidth = CardWidth,
            Height = CardHeight,
            MinHeight = CardHeight,
            MaxHeight = CardHeight,
            Padding = new Thickness(8),
            Background = Card,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = BuildCardContent(item)
        };
        button.Click += async (sender, _) =>
        {
            if (sender is not Button { Tag: ClipboardHistoryItem clicked }) return;
            if (_viewModel.SelectedItem?.Id == clicked.Id)
                await CopyAsync(clicked, true);
            else
            {
                _viewModel.Select(clicked.Id);
                UpdateSelectionVisuals(bringIntoView: true);
            }
        };
        return new CardEntry(item, button);
    }

    private static FrameworkElement BuildCardContent(ClipboardHistoryItem item)
    {
        var content = new Grid { RowSpacing = 6 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FrameworkElement preview;
        if (item.Kind == ClipboardItemKind.Image && TryCreateImage(item.AssetPath, out var image))
        {
            preview = new Border
            {
                Background = Brush(0xEC, 0xF0, 0xF5),
                CornerRadius = new CornerRadius(5),
                Child = image
            };
        }
        else
        {
            preview = new TextBlock
            {
                Text = item.Kind == ClipboardItemKind.Text ? item.Text ?? item.Summary : item.Summary,
                Foreground = Text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = item.Kind == ClipboardItemKind.Text ? 6 : 4
            };
        }
        content.Children.Add(preview);

        var footer = new Grid { ColumnSpacing = 4 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = item.SourceApplication ?? KindName(item.Kind),
            Foreground = Muted,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (item.IsPinned)
        {
            var pinned = new FontIcon { Glyph = "\uE840", FontSize = 10, Foreground = Accent };
            Grid.SetColumn(pinned, 1);
            footer.Children.Add(pinned);
        }
        Grid.SetRow(footer, 1);
        content.Children.Add(footer);
        ToolTipService.SetToolTip(content, $"{item.ResolvedDisplayName}\n{item.Summary}");
        return content;
    }

    private static bool TryCreateImage(string? path, out Image image)
    {
        image = null!;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            image = new Image
            {
                Source = new BitmapImage(new Uri(Path.GetFullPath(path), UriKind.Absolute))
                {
                    DecodePixelWidth = (int)(CardWidth * 2)
                },
                Stretch = Stretch.UniformToFill
            };
            return true;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard thumbnail failed: {error.Message}");
            return false;
        }
    }

    private void UpdateSelectionVisuals(bool bringIntoView)
    {
        var selectedId = _viewModel.SelectedItem?.Id;
        foreach (var pair in _cardCache)
        {
            var selected = pair.Key == selectedId;
            pair.Value.Button.BorderBrush = selected ? Accent : Border;
            pair.Value.Button.BorderThickness = new Thickness(selected ? 2 : 1);
        }
        if (bringIntoView && selectedId is { } id && _cardCache.TryGetValue(id, out var entry))
            entry.Button.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
    }

    private async Task CopyAsync(ClipboardHistoryItem item, bool close)
    {
        try
        {
            if (item.Kind == ClipboardItemKind.Text && item.Text is not null)
                _clipboard.WriteText(item.Text);
            else if (item.Kind == ClipboardItemKind.Image && item.AssetPath is not null)
                await _clipboard.WritePngAsync(await File.ReadAllBytesAsync(item.AssetPath));
            else if (item.Kind == ClipboardItemKind.File && item.FilePaths is { Count: > 0 })
                await _clipboard.WriteFilesAsync(item.FilePaths);
            else
                return;

            if (!close) return;
            if (_paste is not null)
                await _paste(CancellationToken.None);
            else
                RequestClose();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard copy failed: {error}");
        }
    }

    private async Task DeleteAsync(long id)
    {
        try
        {
            _viewModel.Delete(id);
            SynchronizeCards();
            if (_delete is not null)
                await _delete(id, CancellationToken.None);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard delete failed: {error}");
        }
    }

    private async Task ToggleSelectedPinnedAsync()
    {
        if (_viewModel.SelectedItem is not { } item) return;
        try
        {
            _viewModel.TogglePinned(item.Id);
            SynchronizeCards();
            if (_togglePinned is not null)
                await _togglePinned(item.Id, CancellationToken.None);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard pin failed: {error}");
        }
    }

    private async void HandleSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            switch (e.Key)
            {
                case VirtualKey.Down:
                    _viewModel.MoveSelection(1);
                    UpdateSelectionVisuals(bringIntoView: true);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter when _viewModel.SelectedItem is { } item:
                    e.Handled = true;
                    await CopyAsync(item, true);
                    break;
                case VirtualKey.Escape when !string.IsNullOrEmpty(_searchBox?.Text):
                    _searchBox!.Text = string.Empty;
                    e.Handled = true;
                    break;
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard search key failed: {error}");
        }
    }

    private async void HandleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, _searchBox)) return;
        try
        {
            switch (e.Key)
            {
                case VirtualKey.Left: _viewModel.MoveSelection(-1); break;
                case VirtualKey.Right: _viewModel.MoveSelection(1); break;
                case VirtualKey.Enter when _viewModel.SelectedItem is { } item:
                    e.Handled = true;
                    await CopyAsync(item, true);
                    return;
                case VirtualKey.Delete when _viewModel.SelectedItem is { } item:
                    e.Handled = true;
                    await DeleteAsync(item.Id);
                    return;
                case VirtualKey.P:
                    e.Handled = true;
                    await ToggleSelectedPinnedAsync();
                    return;
                case VirtualKey.Escape:
                    e.Handled = true;
                    RequestClose();
                    return;
                default: return;
            }
            e.Handled = true;
            UpdateSelectionVisuals(bringIntoView: true);
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard key failed: {error}");
        }
    }

    private static string KindName(ClipboardItemKind kind) => kind switch
    {
        ClipboardItemKind.Text => "文本",
        ClipboardItemKind.Image => "图片",
        _ => "文件"
    };

    private static SolidColorBrush Brush(byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(0xFF, r, g, b));

    private sealed class CardEntry(ClipboardHistoryItem item, Button button)
    {
        public ClipboardHistoryItem Item { get; set; } = item;
        public Button Button { get; } = button;
    }
}
