using Index.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Gallery;

/// <summary>可复用的图库卡片。视觉树只构建一次，滚动时只重新绑定数据。</summary>
internal sealed class ShotCardView : UserControl, IDisposable
{
    private readonly GalleryTheme _theme;
    private readonly Border _surface;
    private readonly Border _selectionBadge;
    private readonly Border _dimensionBar;
    private readonly TextBlock _dimensionText;
    private readonly ShotAssetThumbnailView _thumbnail;
    private readonly Grid _thumbnailLayer;
    private Grid _headerRow = null!;
    private TextBlock _headerLabel = null!;
    private StackPanel _source = null!;
    private TextBlock _timeText = null!;
    private ContentControl _appIconHost = null!;
    private string? _boundAppIdentifier;
    private string? _boundAppName;
    private string? _boundSourceUrl;
    private readonly TextBlock _captionTitle;
    private readonly TextBlock _captionSubtitle;
    private bool _isFocused;
    private bool _isPointerOver;
    private bool _isSelected;

    public ShotCardView(
        ShotRecord shot,
        CardAppearance appearance,
        GalleryTheme theme,
        double thumbnailHeight,
        IShotAssetReader assets)
    {
        _theme = theme;
        Shot = shot;
        Appearance = appearance;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        _thumbnail = new ShotAssetThumbnailView(assets, shot)
        {
            Height = thumbnailHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _thumbnailLayer = new Grid { Height = thumbnailHeight };
        _thumbnailLayer.Children.Add(new Border
        {
            Background = theme.ThumbnailBackground,
            Child = _thumbnail
        });

        _dimensionText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _dimensionBar = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xC8, 0x18, 0x1B, 0x22)),
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = Visibility.Collapsed,
            Child = _dimensionText
        };
        _thumbnailLayer.Children.Add(_dimensionBar);

        _selectionBadge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = theme.Accent,
            BorderBrush = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "✓",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _thumbnailLayer.Children.Add(_selectionBadge);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(MakeHeader());
        Grid.SetRow(_thumbnailLayer, 1);
        content.Children.Add(_thumbnailLayer);

        _captionTitle = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _captionSubtitle = new TextBlock
        {
            FontSize = 11,
            Foreground = theme.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var caption = new StackPanel
        {
            Spacing = 1,
            Padding = new Thickness(9, 8, 9, 9),
            Children = { _captionTitle, _captionSubtitle }
        };
        Grid.SetRow(caption, 2);
        content.Children.Add(caption);

        _surface = new Border
        {
            Background = theme.Card,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
        var button = new Button
        {
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = _surface
        };
        button.Click += (_, _) => Invoked?.Invoke(this, EventArgs.Empty);
        button.DoubleTapped += (_, _) => PreviewRequested?.Invoke(this, EventArgs.Empty);
        button.PointerEntered += (_, _) => SetHover(true);
        button.PointerExited += (_, _) => SetHover(false);
        button.GotFocus += (_, _) =>
        {
            _isFocused = true;
            UpdateSurfaceBorder();
        };
        button.LostFocus += (_, _) =>
        {
            _isFocused = false;
            UpdateSurfaceBorder();
        };
        Content = button;
        Bind(shot, appearance);
    }

    public ShotRecord Shot { get; private set; }
    public CardAppearance Appearance { get; private set; }
    public event EventHandler? Invoked;
    public event EventHandler? PreviewRequested;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            _selectionBadge.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            UpdateSurfaceBorder();
        }
    }

    public void Bind(ShotRecord shot, CardAppearance appearance)
    {
        Shot = shot;
        Appearance = appearance;
        _dimensionText.Text = $"{shot.PixelWidth} × {shot.PixelHeight}";
        _headerRow.Background = HeaderBrush(appearance.HeaderColorRole);
        _headerLabel.Text = appearance.HeaderLabel;
        _captionTitle.Text = appearance.CaptionTitle;
        _captionSubtitle.Text = appearance.CaptionSubtitle;
        _timeText.Text = shot.CapturedAt.ToLocalTime().ToString("HH:mm");
        if (!string.Equals(_boundAppIdentifier, shot.AppIdentifier, StringComparison.Ordinal) ||
            !string.Equals(_boundAppName, shot.AppName, StringComparison.Ordinal) ||
            !string.Equals(_boundSourceUrl, shot.SourceUrl, StringComparison.Ordinal))
        {
            if (_appIconHost.Content is IDisposable disposable)
                disposable.Dispose();
            _boundAppIdentifier = shot.AppIdentifier;
            _boundAppName = shot.AppName;
            _boundSourceUrl = shot.SourceUrl;
            _appIconHost.Content = string.IsNullOrWhiteSpace(shot.AppIdentifier)
                ? null
                : new SourceIconView(shot.AppIdentifier, shot.AppName, shot.SourceUrl);
        }
        _thumbnail.Bind(shot);
        IsSelected = false;
    }

    private Border MakeHeader()
    {
        _headerRow = new Grid
        {
            Height = 24,
            Padding = new Thickness(9, 0, 9, 0)
        };
        _headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _headerLabel = new TextBlock
        {
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        _headerRow.Children.Add(_headerLabel);
        _source = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        _timeText = new TextBlock
        {
            FontSize = 9,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 92
        };
        _appIconHost = new ContentControl
        {
            Padding = new Thickness(0),
            IsTabStop = false
        };
        _source.Children.Add(_timeText);
        _source.Children.Add(_appIconHost);
        Grid.SetColumn(_source, 1);
        _headerRow.Children.Add(_source);
        return new Border { Child = _headerRow };
    }

    private Brush HeaderBrush(CardHeaderColorRole role) => role switch
    {
        CardHeaderColorRole.Recording => _theme.RecordingHeader,
        _ => _theme.ScreenshotHeader
    };

    private void SetHover(bool hovering)
    {
        _isPointerOver = hovering;
        _dimensionBar.Visibility = hovering ? Visibility.Visible : Visibility.Collapsed;
        UpdateSurfaceBorder();
    }

    private void UpdateSurfaceBorder()
    {
        _surface.BorderBrush = _isSelected || _isFocused
            ? _theme.Accent
            : _isPointerOver
                ? _theme.HoverBorder
                : _theme.CardBorder;
        _surface.BorderThickness = _isSelected || _isFocused
            ? new Thickness(2)
            : new Thickness(1);
    }

    public void SetResponsiveWidth(double width)
    {
        const double thumbnailHeightPerWidth = 144d / 200d;
        var height = Math.Max(1, width * thumbnailHeightPerWidth);
        _thumbnailLayer.Height = height;
        _thumbnail.Height = height;
    }

    public void SetThumbnailLoadingEnabled(bool enabled)
        => _thumbnail.SetLoadingEnabled(enabled);

    public void Dispose()
    {
        _thumbnail.Dispose();
        if (_appIconHost.Content is IDisposable disposable)
            disposable.Dispose();
    }
}
