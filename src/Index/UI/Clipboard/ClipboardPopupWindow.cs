using Index.Clipboard;
using Index.Platform.Clipboard;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Index.UI.Clipboard;

/// <summary>Stable second-window shell for clipboard history.</summary>
public sealed class ClipboardPopupWindow : Window
{
    private const double CardWidth = 104;
    private const double CardHeight = 108;
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
    private ScrollViewer? _stableScroller;
    private bool _initialized;
    private bool _isWindowPinned;
    private bool _hasPositionedCards;
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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 290));
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
        RefreshStableCards();
    }

    public void ApplyWindowChrome() => ClipboardPopupChrome.ApplyStandard(this);

    public void Refresh() => RefreshStableCards();

    private Grid BuildStableRoot()
    {
        var root = new Grid { Background = Panel, Padding = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dragRegion = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
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
            Text = "← → 选择  ·  Enter 复制  ·  Delete 删除  ·  Esc 关闭",
            Foreground = Muted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        dragRegion.Children.Add(heading);
        header.Children.Add(dragRegion);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var pinButton = BuildHeaderButton("\uE840", "钉住弹窗");
        pinButton.Click += (_, _) =>
        {
            _isWindowPinned = !_isWindowPinned;
            pinButton.Background = _isWindowPinned ? Accent : new SolidColorBrush(Colors.Transparent);
            pinButton.Foreground = _isWindowPinned ? new SolidColorBrush(Colors.White) : Muted;
            ToolTipService.SetToolTip(pinButton, _isWindowPinned ? "取消钉住" : "钉住弹窗");
        };
        actions.Children.Add(pinButton);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);
        var scroller = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _cards
        };
        _stableScroller = scroller;
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        return root;
    }

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

    private void RefreshStableCards()
    {
        if (!_initialized) return;
        _cards.Children.Clear();
        var items = _viewModel.VisibleItems;
        if (items.Count == 0)
        {
            _cards.Children.Add(new TextBlock
            {
                Text = "暂无剪贴板历史",
                Foreground = Muted,
                Margin = new Thickness(12, 80, 0, 0)
            });
            return;
        }
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var selected = index == _viewModel.SelectedIndex;
            var label = item.Kind == ClipboardItemKind.Text
                ? item.Text ?? item.Summary
                : $"{KindName(item.Kind)}\n\n{item.Summary}";
            var button = new Button
            {
                Width = CardWidth,
                MinWidth = CardWidth,
                MaxWidth = CardWidth,
                Height = CardHeight,
                MinHeight = CardHeight,
                MaxHeight = CardHeight,
                Padding = new Thickness(8),
                Background = Card,
                BorderBrush = selected ? Accent : Border,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = new TextBlock
                {
                    Text = label,
                    Foreground = Text,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 5
                }
            };
            button.Click += async (_, _) =>
            {
                if (_viewModel.SelectedItem?.Id == item.Id)
                    await CopyAsync(item, true);
                else
                {
                    _viewModel.Select(item.Id);
                    RefreshStableCards();
                }
            };
            _cards.Children.Add(button);
        }
        if (!_hasPositionedCards)
        {
            _hasPositionedCards = true;
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _stableScroller?.ChangeView(0, null, null, true));
        }
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
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard copy failed: {error}");
            return;
        }
        if (close)
        {
            if (_paste is not null)
                await _paste(CancellationToken.None);
            else
                RequestClose();
        }
    }

    private async Task DeleteAsync(long id)
    {
        _viewModel.Delete(id);
        RefreshStableCards();
        if (_delete is not null) await _delete(id, CancellationToken.None);
    }

    private async void HandleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox && e.Key != VirtualKey.Escape) return;
        switch (e.Key)
        {
            case VirtualKey.Left: _viewModel.MoveSelection(-1); break;
            case VirtualKey.Right: _viewModel.MoveSelection(1); break;
            case VirtualKey.Enter when _viewModel.SelectedItem is { } item:
                await CopyAsync(item, true);
                return;
            case VirtualKey.Delete when _viewModel.SelectedItem is { } item:
                await DeleteAsync(item.Id);
                break;
            case VirtualKey.Escape: RequestClose(); return;
            default: return;
        }
        e.Handled = true;
        RefreshStableCards();
    }

    private static string KindName(ClipboardItemKind kind) => kind switch
    {
        ClipboardItemKind.Text => "文本",
        ClipboardItemKind.Image => "图片",
        _ => "文件"
    };

    private static SolidColorBrush Brush(byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(0xFF, r, g, b));
}
