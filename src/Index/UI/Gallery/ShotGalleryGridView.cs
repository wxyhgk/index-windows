using System.Collections.ObjectModel;
using Index.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace Index.UI.Gallery;

/// <summary>
/// 游标分页的虚拟化截图网格。ItemsRepeater 只创建视口附近的行，每行固定四张卡片。
/// </summary>
internal sealed class ShotGalleryGridView : UserControl, IDisposable
{
    private const int Columns = 4;
    private const int PageSize = 300;
    private const double Spacing = 14;
    private const double ThumbnailHeight = 144;

    private readonly ObservableCollection<ShotRow> _rows = [];
    private readonly List<ShotRecord> _loadedShots = [];
    private readonly ShotStore _store;
    private readonly GalleryTheme _theme;
    private readonly ScrollViewer _scrollViewer;
    private readonly ItemsRepeater _repeater;
    private readonly DispatcherQueueTimer _resizeTimer;
    private readonly DispatcherQueueTimer _scrollIdleTimer;
    private readonly Dictionary<long, ShotCardView> _realizedCards = new();
    private readonly HashSet<ShotCardView> _allCards = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private ShotPageCursor? _nextCursor;
    private bool _isLoadingPage;
    private bool _thumbnailLoadingEnabled = true;
    private double _responsiveCardWidth = 200;
    private long? _selectedId;
    private bool _disposed;

    public ShotGalleryGridView(
        ShotPage firstPage,
        ShotStore store,
        GalleryTheme theme)
    {
        _nextCursor = firstPage.NextCursor;
        _store = store;
        _theme = theme;
        AppendRows(firstPage.Items);
        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick += (_, _) => ApplyResponsiveCardWidth();
        _scrollIdleTimer = DispatcherQueue.CreateTimer();
        _scrollIdleTimer.Interval = TimeSpan.FromMilliseconds(120);
        _scrollIdleTimer.IsRepeating = false;
        _scrollIdleTimer.Tick += (_, _) => ResumeThumbnailLoading();

        _repeater = new ItemsRepeater
        {
            ItemsSource = _rows,
            ItemTemplate = new ShotRowElementFactory(this),
            HorizontalCacheLength = 0,
            VerticalCacheLength = 0.25,
            Layout = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = Spacing
            },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _scrollViewer = new ScrollViewer
        {
            Content = _repeater,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        _scrollViewer.ViewChanged += OnViewChanged;
        PreviewKeyDown += OnKeyDown;
        Unloaded += OnUnloaded;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        IsTabStop = true;
        Content = _scrollViewer;
    }

    private void AppendRows(IReadOnlyList<ShotRecord> shots)
    {
        _loadedShots.AddRange(shots);
        for (var start = 0; start < shots.Count; start += Columns)
            _rows.Add(new ShotRow(shots.Skip(start).Take(Columns).ToArray()));
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        SuspendThumbnailLoading();
        if (_nextCursor is null || _isLoadingPage || _scrollViewer.VerticalOffset <= 1)
            return;

        var remaining = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        if (remaining <= Math.Max(600, _scrollViewer.ViewportHeight * 1.5))
            _ = LoadNextPageAsync();
    }

    private async Task LoadNextPageAsync()
    {
        if (_isLoadingPage || _nextCursor is not { } cursor)
            return;

        _isLoadingPage = true;
        try
        {
            var page = await _store.GetPageAsync(
                PageSize,
                cursor,
                _lifetimeCancellation.Token);
            if (_disposed)
                return;
            AppendRows(page.Items);
            _nextCursor = page.NextCursor;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _isLoadingPage = false;
        }
    }

    private ShotCardView CreateCard(ShotRecord shot)
    {
        var card = new ShotCardView(
            shot,
            CardAppearance.ForShot(shot),
            _theme,
            ThumbnailHeight,
            _store.ThumbnailPath(shot),
            _store.LegacyThumbnailPath(shot),
            _store.OriginalPath(shot))
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsSelected = shot.Id == _selectedId
        };
        card.Invoked += OnCardInvoked;
        card.PreviewRequested += OnCardPreviewRequested;
        card.SetResponsiveWidth(_responsiveCardWidth);
        card.SetThumbnailLoadingEnabled(_thumbnailLoadingEnabled);
        _realizedCards[shot.Id] = card;
        _allCards.Add(card);
        _ = EnsureLosslessThumbnailAsync(shot, card);
        return card;
    }

    private void SuspendThumbnailLoading()
    {
        _scrollIdleTimer.Stop();
        _thumbnailLoadingEnabled = false;
        foreach (var card in _realizedCards.Values)
            card.SetThumbnailLoadingEnabled(false);
        _scrollIdleTimer.Start();
    }

    private void ResumeThumbnailLoading()
    {
        _thumbnailLoadingEnabled = true;
        foreach (var card in _realizedCards.Values)
            card.SetThumbnailLoadingEnabled(true);
    }

    private void ScheduleResponsiveCardWidth(double rowWidth)
    {
        _responsiveCardWidth = Math.Max(1, (rowWidth - (Spacing * (Columns - 1))) / Columns);
        if (!_resizeTimer.IsRunning)
            _resizeTimer.Start();
    }

    private void ApplyResponsiveCardWidth()
    {
        foreach (var card in _realizedCards.Values)
            card.SetResponsiveWidth(_responsiveCardWidth);
    }

    private void BindCard(ShotCardView card, ShotRecord shot)
    {
        if (_realizedCards.TryGetValue(card.Shot.Id, out var current) && ReferenceEquals(current, card))
            _realizedCards.Remove(card.Shot.Id);
        card.Bind(
            shot,
            CardAppearance.ForShot(shot),
            _store.ThumbnailPath(shot),
            _store.LegacyThumbnailPath(shot),
            _store.OriginalPath(shot));
        card.SetResponsiveWidth(_responsiveCardWidth);
        card.SetThumbnailLoadingEnabled(_thumbnailLoadingEnabled);
        card.IsSelected = shot.Id == _selectedId;
        card.Visibility = Visibility.Visible;
        _realizedCards[shot.Id] = card;
        _ = EnsureLosslessThumbnailAsync(shot, card);
    }

    private async Task EnsureLosslessThumbnailAsync(ShotRecord shot, ShotCardView card)
    {
        try
        {
            if (!await _store.EnsureLosslessThumbnailAsync(
                    shot,
                    _lifetimeCancellation.Token))
                return;

            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_disposed && card.Shot.Id == shot.Id)
                    {
                        card.SetThumbnailPaths(
                            _store.ThumbnailPath(shot),
                            _store.LegacyThumbnailPath(shot),
                            _store.OriginalPath(shot));
                    }
                }))
            {
                return;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Lossless thumbnail migration failed for shot {shot.Id}: {error.Message}");
        }
    }

    private void UnrealizeCard(ShotCardView card)
    {
        if (_realizedCards.TryGetValue(card.Shot.Id, out var current) && ReferenceEquals(current, card))
            _realizedCards.Remove(card.Shot.Id);
        card.SetThumbnailLoadingEnabled(false);
    }

    private void OnCardInvoked(object? sender, EventArgs e)
    {
        if (sender is not ShotCardView selected)
            return;
        SelectShot(selected.Shot, bringIntoView: false);
    }

    private void OnCardPreviewRequested(object? sender, EventArgs e)
    {
        if (sender is not ShotCardView selected)
            return;
        SelectShot(selected.Shot, bringIntoView: false);
        PreviewRequested?.Invoke(selected.Shot);
    }

    private void SelectShot(ShotRecord shot, bool bringIntoView)
    {
        _selectedId = shot.Id;
        foreach (var card in _realizedCards.Values)
            card.IsSelected = card.Shot.Id == _selectedId;
        SelectionChanged?.Invoke(shot);

        if (!bringIntoView)
            return;
        var index = _loadedShots.FindIndex(item => item.Id == shot.Id);
        if (index < 0)
            return;
        var row = _repeater.GetOrCreateElement(index / Columns);
        row.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
    }

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space && _loadedShots.Count > 0)
        {
            var selected = _selectedId is { } selectedId
                ? _loadedShots.FirstOrDefault(shot => shot.Id == selectedId)
                : _loadedShots[0];
            if (selected is not null)
            {
                SelectShot(selected, bringIntoView: false);
                PreviewRequested?.Invoke(selected);
            }
            e.Handled = true;
            return;
        }

        var delta = e.Key switch
        {
            VirtualKey.Left => -1,
            VirtualKey.Right => 1,
            VirtualKey.Up => -Columns,
            VirtualKey.Down => Columns,
            _ => 0
        };
        if (delta == 0)
            return;
        e.Handled = true;
        await MoveSelectionAsync(delta);
    }

    public async Task<ShotRecord?> MoveSelectionAsync(int delta)
    {
        if (_loadedShots.Count == 0 || delta == 0)
            return null;
        var currentIndex = _selectedId is { } selectedId
            ? _loadedShots.FindIndex(shot => shot.Id == selectedId)
            : -1;
        var targetIndex = currentIndex < 0 ? 0 : currentIndex + delta;
        if (targetIndex >= _loadedShots.Count && _nextCursor is not null)
            await LoadNextPageAsync();
        targetIndex = Math.Clamp(targetIndex, 0, _loadedShots.Count - 1);
        var shot = _loadedShots[targetIndex];
        SelectShot(shot, bringIntoView: true);
        return shot;
    }

    public event Action<ShotRecord>? SelectionChanged;
    public event Action<ShotRecord>? PreviewRequested;

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _resizeTimer.Stop();
        _scrollIdleTimer.Stop();
        _thumbnailLoadingEnabled = false;
        foreach (var card in _realizedCards.Values)
            card.SetThumbnailLoadingEnabled(false);
    }

    public void SetBackgrounded(bool backgrounded)
    {
        if (_disposed)
            return;

        _scrollIdleTimer.Stop();
        _thumbnailLoadingEnabled = !backgrounded;
        foreach (var card in _realizedCards.Values)
            card.SetThumbnailLoadingEnabled(!backgrounded);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        _resizeTimer.Stop();
        _scrollIdleTimer.Stop();
        _scrollViewer.ViewChanged -= OnViewChanged;
        PreviewKeyDown -= OnKeyDown;
        Unloaded -= OnUnloaded;
        foreach (var card in _allCards)
            card.Dispose();
        _allCards.Clear();
        _realizedCards.Clear();
        _lifetimeCancellation.Dispose();
    }

    private sealed record ShotRow(IReadOnlyList<ShotRecord> Shots);

    private sealed class ShotRowView : Grid
    {
        private readonly ShotGalleryGridView _owner;
        private readonly List<ShotCardView> _cards = [];

        public ShotRowView(ShotGalleryGridView owner, ShotRow row)
        {
            _owner = owner;
            ColumnSpacing = Spacing;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            for (var column = 0; column < Columns; column++)
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Bind(row);
            SizeChanged += OnSizeChanged;
        }

        public void Bind(ShotRow row)
        {
            for (var column = 0; column < row.Shots.Count; column++)
            {
                if (column < _cards.Count)
                {
                    _owner.BindCard(_cards[column], row.Shots[column]);
                    continue;
                }

                var card = _owner.CreateCard(row.Shots[column]);
                Grid.SetColumn(card, column);
                Children.Add(card);
                _cards.Add(card);
            }

            for (var column = row.Shots.Count; column < _cards.Count; column++)
            {
                _owner.UnrealizeCard(_cards[column]);
                _cards[column].Visibility = Visibility.Collapsed;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
            => _owner.ScheduleResponsiveCardWidth(e.NewSize.Width);

        public void Unbind()
        {
            foreach (var card in _cards)
                _owner.UnrealizeCard(card);
        }
    }

    private sealed class ShotRowElementFactory(ShotGalleryGridView owner) : IElementFactory
    {
        private readonly Stack<ShotRowView> _pool = [];

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var row = (ShotRow)args.Data;
            if (!_pool.TryPop(out var view))
                return new ShotRowView(owner, row);
            view.Bind(row);
            return view;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is ShotRowView row)
            {
                row.Unbind();
                _pool.Push(row);
            }
        }
    }
}
