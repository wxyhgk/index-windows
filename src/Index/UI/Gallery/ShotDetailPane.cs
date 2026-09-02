using Index.Gallery;
using Index.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Gallery;

/// <summary>选中截图的详情与快捷动作；不直接访问数据库或平台 API。</summary>
internal sealed class ShotDetailPane : UserControl, IDisposable
{
    private readonly IShotAssetReader _assets;
    private readonly GalleryShotCommandService _commands;
    private readonly GalleryTheme _theme;
    private readonly DispatcherQueue _dispatcher;
    private readonly StackPanel _content;
    private ShotRecord? _shot;
    private int _shotGeneration;
    private ShotAssetThumbnailView? _previewThumbnail;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _disposed;

    public ShotDetailPane(
        IShotAssetReader assets,
        GalleryShotCommandService commands,
        GalleryTheme theme)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _commands = commands;
        _theme = theme;
        _dispatcher = DispatcherQueue;
        Width = 300;
        VerticalAlignment = VerticalAlignment.Stretch;
        _content = new StackPanel { Spacing = 12 };
        Content = new Border
        {
            Background = theme.Card,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                Content = _content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        Unloaded += OnUnloaded;
        ShowEmpty();
    }

    public event Action<ShotRecord>? ShotDeleted;
    public event Action<ShotRecord, bool>? FavoriteChanged;
    public event Action<ShotRecord>? PreviewRequested;

    public void ShowShot(ShotRecord shot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _shot = shot;
        var generation = ++_shotGeneration;
        _previewThumbnail?.Dispose();
        _previewThumbnail = new ShotAssetThumbnailView(_assets, shot);
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = shot.WindowTitle ?? $"截图 {shot.Id}",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var preview = new Button
        {
            Height = 190,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = _theme.ThumbnailBackground,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = _previewThumbnail
        };
        ToolTipService.SetToolTip(preview, "点击预览原图 · Space");
        preview.Click += (_, _) => PreviewRequested?.Invoke(shot);
        _content.Children.Add(preview);
        _content.Children.Add(Metadata("尺寸", $"{shot.PixelWidth} × {shot.PixelHeight}"));
        _content.Children.Add(Metadata("截图时间", shot.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        _content.Children.Add(Metadata("来源应用", shot.AppName ?? "未知"));
        if (!string.IsNullOrWhiteSpace(shot.WindowTitle))
            _content.Children.Add(Metadata("窗口", shot.WindowTitle));
        if (!string.IsNullOrWhiteSpace(shot.SourceUrl))
            _content.Children.Add(Metadata("网页", shot.SourceUrl));

        var favorite = new Button
        {
            Content = "正在读取收藏状态…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false
        };
        var tags = new TextBlock
        {
            FontSize = 12,
            Foreground = _theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        _content.Children.Add(favorite);
        _content.Children.Add(tags);
        _ = LoadOrganizationAsync(shot, favorite, tags, generation);

        var actions = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddAction(actions, "复制", 0, 0,
            () => RunAsync(
                () => _commands.CopyAsync(shot, _lifetimeCancellation.Token),
                "已复制到剪贴板"));
        AddAction(actions, "导出", 1, 0, () => ExportAsync(shot));
        AddAction(actions, "打开原图", 0, 1,
            () => RunAsync(
                () => _commands.OpenOriginalAsync(shot, _lifetimeCancellation.Token),
                "已打开原图"));
        AddAction(actions, "删除", 1, 1, () => ConfirmDeleteAsync(shot), destructive: true);
        if (!string.IsNullOrWhiteSpace(shot.SourceUrl))
        {
            actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddAction(actions, "打开网页", 0, 2,
                () => Run(() => _commands.OpenSource(shot), "已打开来源网页"));
        }
        _content.Children.Add(actions);
        _content.Children.Add(new TextBlock
        {
            Name = "StatusText",
            FontSize = 11,
            Foreground = _theme.Muted,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private async Task LoadOrganizationAsync(
        ShotRecord shot,
        Button favoriteButton,
        TextBlock tagsText,
        int generation)
    {
        try
        {
            var isFavorite = await _commands.IsFavoriteAsync(shot, _lifetimeCancellation.Token);
            var tags = await _commands.GetTagsAsync(shot, _lifetimeCancellation.Token);
            if (generation != _shotGeneration || _shot?.Id != shot.Id) return;

            favoriteButton.Content = isFavorite ? "★ 已收藏" : "☆ 收藏";
            favoriteButton.IsEnabled = true;
            favoriteButton.Click += async (_, _) =>
            {
                favoriteButton.IsEnabled = false;
                try
                {
                    isFavorite = !isFavorite;
                    await _commands.SetFavoriteAsync(
                        shot,
                        isFavorite,
                        _lifetimeCancellation.Token);
                    if (generation != _shotGeneration || _shot?.Id != shot.Id) return;
                    favoriteButton.Content = isFavorite ? "★ 已收藏" : "☆ 收藏";
                    FavoriteChanged?.Invoke(shot, isFavorite);
                    SetStatus(isFavorite ? "已加入快速收藏" : "已取消收藏");
                }
                catch (Exception error)
                {
                    isFavorite = !isFavorite;
                    SetStatus($"收藏操作失败：{error.Message}");
                }
                finally
                {
                    if (generation == _shotGeneration && _shot?.Id == shot.Id)
                        favoriteButton.IsEnabled = true;
                }
            };

            if (tags.Count > 0)
            {
                tagsText.Text = $"标签：{string.Join(" · ", tags)}";
                tagsText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception error)
        {
            if (generation != _shotGeneration || _shot?.Id != shot.Id) return;
            favoriteButton.Content = "收藏状态读取失败";
            SetStatus(error.Message);
        }
    }

    private void ShowEmpty()
    {
        _shot = null;
        _shotGeneration++;
        _previewThumbnail?.Dispose();
        _previewThumbnail = null;
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = "选择一张截图",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text
        });
        _content.Children.Add(new TextBlock
        {
            Text = "查看大图、来源信息并执行快捷操作。",
            FontSize = 12,
            Foreground = _theme.Muted,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private StackPanel Metadata(string label, string value) => new()
    {
        Spacing = 2,
        Children =
        {
            new TextBlock { Text = label, FontSize = 11, Foreground = _theme.Muted },
            new TextBlock { Text = value, FontSize = 13, Foreground = _theme.Text, TextWrapping = TextWrapping.Wrap }
        }
    };

    private void AddAction(
        Grid host,
        string title,
        int column,
        int row,
        Func<Task> action,
        bool destructive = false)
    {
        var button = new Button
        {
            Content = title,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = destructive ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed) : _theme.Text
        };
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                SetStatus($"操作失败：{error.Message}");
            }
        };
        Grid.SetColumn(button, column);
        Grid.SetRow(button, row);
        host.Children.Add(button);
    }

    private async Task ExportAsync(ShotRecord shot)
    {
        await RunAsync(async () =>
        {
            var path = await _commands.ExportAsync(shot, _lifetimeCancellation.Token);
            return $"已导出：{path}";
        });
    }

    private async Task ConfirmDeleteAsync(ShotRecord shot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除这张截图？",
            Content = "原图、缩略图和修订记录都会删除，此操作无法撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            if (await _commands.DeleteAsync(shot, _lifetimeCancellation.Token))
            {
                _ = _dispatcher.TryEnqueue(() => ShotDeleted?.Invoke(shot));
            }
        }
        catch (Exception error)
        {
            _ = _dispatcher.TryEnqueue(() => SetStatus($"删除失败：{error.Message}"));
        }
    }

    private async Task RunAsync(Func<Task> action, string success)
    {
        await RunAsync(async () =>
        {
            await action();
            return success;
        });
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        SetStatus("正在处理…");
        try
        {
            var result = await action();
            _ = _dispatcher.TryEnqueue(() => SetStatus(result));
        }
        catch (Exception error)
        {
            _ = _dispatcher.TryEnqueue(() => SetStatus($"操作失败：{error.Message}"));
        }
    }

    private Task Run(Action action, string success)
    {
        try
        {
            action();
            SetStatus(success);
            return Task.CompletedTask;
        }
        catch (Exception error)
        {
            SetStatus($"操作失败：{error.Message}");
            return Task.CompletedTask;
        }
    }

    private void SetStatus(string message)
    {
        if (_disposed)
            return;
        var status = _content.Children.OfType<TextBlock>().FirstOrDefault(child => child.Name == "StatusText");
        if (status is not null) status.Text = message;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _shotGeneration++;
        _previewThumbnail?.Dispose();
        _previewThumbnail = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        Unloaded -= OnUnloaded;
        _shotGeneration++;
        _previewThumbnail?.Dispose();
        _previewThumbnail = null;
        _lifetimeCancellation.Dispose();
    }
}
