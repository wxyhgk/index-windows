using Index.Storage;
using Index.UI.Gallery;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Applications;

internal sealed class ApplicationCardView : UserControl, IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private readonly Border _surface;
    private bool _disposed;

    public ApplicationCardView(
        CapturedApplicationSummary application,
        bool featured,
        IShotAssetReader assets,
        GalleryTheme theme)
    {
        Application = application;
        var content = new StackPanel { Spacing = featured ? 12 : 0 };
        content.Children.Add(CreateIdentity(application, featured ? 34 : 30, theme));
        if (featured)
            content.Children.Add(CreatePreviewStrip(application, assets, theme));

        _surface = new Border
        {
            Background = theme.Card,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = content
        };
        var button = new Button
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(12),
            UseSystemFocusVisuals = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = _surface
        };
        var focused = false;
        button.Click += (_, _) => Invoked?.Invoke(Application);
        button.PointerEntered += (_, _) =>
            _surface.BorderBrush = focused ? theme.Accent : theme.HoverBorder;
        button.PointerExited += (_, _) =>
            _surface.BorderBrush = focused ? theme.Accent : theme.CardBorder;
        button.GotFocus += (_, _) =>
        {
            focused = true;
            _surface.BorderBrush = theme.Accent;
            _surface.BorderThickness = new Thickness(2);
        };
        button.LostFocus += (_, _) =>
        {
            focused = false;
            _surface.BorderBrush = theme.CardBorder;
            _surface.BorderThickness = new Thickness(1);
        };
        ToolTipService.SetToolTip(button, $"打开 {application.Name} 的截图");
        Content = button;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public CapturedApplicationSummary Application { get; }
    public event Action<CapturedApplicationSummary>? Invoked;

    private FrameworkElement CreateIdentity(
        CapturedApplicationSummary application,
        double iconSide,
        GalleryTheme theme)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        FrameworkElement icon;
        if (application.AppIdentifier is { } identifier)
        {
            var applicationIcon = new AppIconView(identifier, application.Name, iconSide);
            _resources.Add(applicationIcon);
            icon = applicationIcon;
        }
        else
        {
            icon = new Border
            {
                Width = iconSide,
                Height = iconSide,
                CornerRadius = new CornerRadius(8),
                Background = theme.Selected,
                Child = new FontIcon
                {
                    Glyph = "\uECAA",
                    FontSize = iconSide * 0.55,
                    Foreground = theme.Muted
                }
            };
        }
        row.Children.Add(icon);

        var labels = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = application.Name,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = theme.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = $"{application.CaptureCount:N0} 张 · 最近 {application.LastCapturedAt.ToLocalTime():MM-dd HH:mm}",
                    FontSize = 11,
                    Foreground = theme.Muted,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        return row;
    }

    private FrameworkElement CreatePreviewStrip(
        CapturedApplicationSummary application,
        IShotAssetReader assets,
        GalleryTheme theme)
    {
        var previews = new Grid { ColumnSpacing = 8, Height = 74 };
        for (var column = 0; column < 3; column++)
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var column = 0; column < 3; column++)
        {
            FrameworkElement child;
            if (column < application.Previews.Count)
            {
                var thumbnail = new ShotAssetThumbnailView(assets, application.Previews[column]);
                _resources.Add(thumbnail);
                child = new Border
                {
                    Background = theme.ThumbnailBackground,
                    CornerRadius = new CornerRadius(7),
                    Child = thumbnail
                };
            }
            else
            {
                child = new Border
                {
                    Background = theme.ThumbnailBackground,
                    CornerRadius = new CornerRadius(7)
                };
            }
            Grid.SetColumn(child, column);
            previews.Children.Add(child);
        }
        return previews;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var resource in _resources)
            resource.Dispose();
        _resources.Clear();
    }
}

internal sealed class ResponsiveApplicationCardGrid : Grid, IDisposable
{
    private const double Spacing = 12;
    private readonly IReadOnlyList<ApplicationCardView> _cards;
    private readonly double _targetCardWidth;
    private int _columns;
    private int? _pendingColumns;
    private bool _rebuildQueued;
    private bool _disposed;

    public ResponsiveApplicationCardGrid(
        IReadOnlyList<CapturedApplicationSummary> applications,
        bool featured,
        IShotAssetReader assets,
        GalleryTheme theme,
        Action<CapturedApplicationSummary> open)
    {
        _targetCardWidth = featured ? 280 : 210;
        _cards = applications.Select(application =>
        {
            var card = new ApplicationCardView(application, featured, assets, theme);
            card.Invoked += open;
            return card;
        }).ToArray();
        ColumnSpacing = Spacing;
        RowSpacing = Spacing;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        SizeChanged += OnSizeChanged;
        Rebuild(1);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.NewSize.Width <= 0)
            return;
        var layout = GalleryLayout.Calculate(new GalleryLayoutRequest(
            StableContainerWidth: args.NewSize.Width,
            TargetCardWidth: _targetCardWidth,
            HorizontalInset: 0,
            Spacing: Spacing,
            ScrollbarReserve: 0,
            MinimumColumnWidth: 160,
            MinimumTargetFillRatio: 0.72));
        QueueRebuild(layout.ColumnCount);
    }

    private void QueueRebuild(int columns)
    {
        if (_disposed || columns == _columns)
            return;
        _pendingColumns = columns;
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;
        if (!DispatcherQueue.TryEnqueue(ApplyPendingRebuild))
            _rebuildQueued = false;
    }

    private void ApplyPendingRebuild()
    {
        _rebuildQueued = false;
        if (_disposed || _pendingColumns is not { } columns)
            return;
        _pendingColumns = null;
        Rebuild(columns);
    }

    private void Rebuild(int columns)
    {
        if (_columns == columns && Children.Count == _cards.Count)
            return;
        _columns = Math.Max(1, columns);
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        for (var column = 0; column < _columns; column++)
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = Math.Max(1, (int)Math.Ceiling(_cards.Count / (double)_columns));
        for (var row = 0; row < rows; row++)
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            Grid.SetColumn(card, index % _columns);
            Grid.SetRow(card, index / _columns);
            Children.Add(card);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pendingColumns = null;
        SizeChanged -= OnSizeChanged;
        foreach (var card in _cards)
            card.Dispose();
    }
}
