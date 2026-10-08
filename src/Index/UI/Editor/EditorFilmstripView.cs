using Index.Editor;
using Index.Storage;
using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Editor;

/// <summary>
/// A lightweight, bounded filmstrip projection. Selection is only requested here;
/// the workspace host owns flush-before-switch and commits via SetCurrentShot.
/// </summary>
internal sealed class EditorFilmstripView : UserControl, IDisposable
{
    private const int NeighborRadius = 12;
    private readonly EditorFilmstripController _controller;
    private readonly IShotAssetReader _assets;
    private readonly GalleryTheme _theme;
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer _scroll;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly ProgressRing _progress;
    private readonly TextBlock _status;
    private readonly List<ShotAssetThumbnailView> _thumbnails = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private long[] _renderedIds = [];
    private bool _loaded;
    private bool _disposed;

    public EditorFilmstripView(
        EditorFilmstripController controller,
        IShotAssetReader assets,
        GalleryTheme theme)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        _previous = NavigationButton("\uE76B", "上一张");
        _next = NavigationButton("\uE76C", "下一张");
        _previous.Click += OnPreviousClicked;
        _next.Click += OnNextClicked;
        _progress = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed
        };
        _status = new TextBlock
        {
            Foreground = _theme.Muted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _scroll = new ScrollViewer
        {
            Content = _items,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _scroll.PointerWheelChanged += OnPointerWheelChanged;

        var root = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(12, 8, 12, 8),
            Background = _theme.Panel
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(_previous);
        Grid.SetColumn(_scroll, 1);
        root.Children.Add(_scroll);
        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        trailing.Children.Add(_status);
        trailing.Children.Add(_progress);
        trailing.Children.Add(_next);
        Grid.SetColumn(trailing, 2);
        root.Children.Add(trailing);
        Content = root;

        _controller.StateChanged += OnControllerStateChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Render(_controller.State);
    }

    public event Action<ShotRecord>? ShotSelected;

    /// <summary>Called by the host only after the old editing session was flushed.</summary>
    public void SetCurrentShot(ShotRecord shot) => _controller.SetCurrentShot(shot);

    public Task ReloadAsync(CancellationToken cancellationToken = default)
        => _controller.ReloadAsync(cancellationToken);

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed)
            return;
        foreach (var thumbnail in _thumbnails)
            thumbnail.SetLoadingEnabled(true);
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            await _controller.ReloadAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Editor filmstrip load failed: {error}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        foreach (var thumbnail in _thumbnails)
            thumbnail.SetLoadingEnabled(false);
    }

    private async void OnPreviousClicked(object sender, RoutedEventArgs args)
        => await RequestAdjacentAsync(-1);

    private async void OnNextClicked(object sender, RoutedEventArgs args)
        => await RequestAdjacentAsync(1);

    private async Task RequestAdjacentAsync(int direction)
    {
        try
        {
            var shot = await _controller.GetAdjacentAsync(direction, _lifetimeCancellation.Token);
            if (_disposed || shot is null)
                return;
            if (DispatcherQueue.HasThreadAccess)
            {
                ShotSelected?.Invoke(shot);
            }
            else if (!DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_disposed)
                        ShotSelected?.Invoke(shot);
                }))
            {
                System.Diagnostics.Debug.WriteLine(
                    "Editor filmstrip selection dispatcher is unavailable.");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Editor filmstrip navigation failed: {error}");
        }
    }

    private void OnControllerStateChanged(EditorFilmstripState state)
    {
        if (_disposed)
            return;
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                    Render(state);
            }))
        {
            System.Diagnostics.Debug.WriteLine("Editor filmstrip dispatcher is unavailable.");
        }
    }

    private void Render(EditorFilmstripState state)
    {
        _previous.IsEnabled = !state.IsLoading && state.CurrentIndex > 0;
        _next.IsEnabled = !state.IsLoading
            && state.CurrentIndex >= 0
            && (state.CurrentIndex < state.Items.Count - 1 || state.HasOlderItems);
        _progress.Visibility = state.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = state.Error ?? string.Empty;
        _status.Visibility = string.IsNullOrWhiteSpace(state.Error)
            ? Visibility.Collapsed
            : Visibility.Visible;

        var start = Math.Max(0, state.CurrentIndex - NeighborRadius);
        var end = Math.Min(state.Items.Count, state.CurrentIndex + NeighborRadius + 1);
        var visible = state.Items.Skip(start).Take(Math.Max(0, end - start)).ToArray();
        var ids = visible.Select(shot => shot.Id).ToArray();
        if (!_renderedIds.SequenceEqual(ids))
        {
            Rebuild(visible);
            _renderedIds = ids;
        }

        foreach (var element in _items.Children.OfType<Button>())
        {
            if (element.Tag is not ShotRecord shot || element.Content is not Border border)
                continue;
            var selected = shot.Id == state.CurrentShotId;
            border.BorderBrush = selected ? _theme.Accent : _theme.CardBorder;
            border.BorderThickness = new Thickness(selected ? 2 : 1);
            if (selected)
                element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        }
    }

    private void Rebuild(IReadOnlyList<ShotRecord> shots)
    {
        foreach (var button in _items.Children.OfType<Button>())
            button.Click -= OnShotClicked;
        foreach (var thumbnail in _thumbnails)
            thumbnail.Dispose();
        _thumbnails.Clear();
        _items.Children.Clear();

        foreach (var shot in shots)
        {
            var thumbnail = new ShotAssetThumbnailView(_assets, shot)
            {
                Width = 104,
                Height = 58
            };
            _thumbnails.Add(thumbnail);
            var border = new Border
            {
                Width = 112,
                Height = 66,
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(7),
                BorderBrush = _theme.CardBorder,
                BorderThickness = new Thickness(1),
                Background = _theme.ThumbnailBackground,
                Child = thumbnail
            };
            var button = new Button
            {
                Tag = shot,
                Content = border,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += OnShotClicked;
            ToolTipService.SetToolTip(
                button,
                $"{shot.AppName ?? "截图"} · {shot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            _items.Children.Add(button);
        }
    }

    private void OnShotClicked(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: ShotRecord shot }
            && shot.Id != _controller.State.CurrentShotId)
        {
            ShotSelected?.Invoke(shot);
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        var delta = args.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
        if (delta == 0 || _scroll.ScrollableWidth <= 0)
            return;
        _scroll.ChangeView(
            Math.Clamp(_scroll.HorizontalOffset - delta, 0, _scroll.ScrollableWidth),
            null,
            null,
            true);
        args.Handled = true;
    }

    private Button NavigationButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14
            }
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        _controller.StateChanged -= OnControllerStateChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _previous.Click -= OnPreviousClicked;
        _next.Click -= OnNextClicked;
        _scroll.PointerWheelChanged -= OnPointerWheelChanged;
        foreach (var button in _items.Children.OfType<Button>())
            button.Click -= OnShotClicked;
        foreach (var thumbnail in _thumbnails)
            thumbnail.Dispose();
        _thumbnails.Clear();
        _controller.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
