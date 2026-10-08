using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Editor;

/// <summary>Stateless editor status and output command surface.</summary>
internal sealed class EditorFooterView : UserControl
{
    private readonly GalleryTheme _theme;
    private readonly TextBlock _zoom = new();
    private readonly TextBlock _warning = new();

    public EditorFooterView(GalleryTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Content = Build();
    }

    public event Action? ZoomOutRequested;
    public event Action? FitRequested;
    public event Action? ZoomInRequested;
    public event Action? CopyRequested;
    public event Action? ExportRequested;

    public string Warning
    {
        get => _warning.Text;
        set => _warning.Text = value ?? string.Empty;
    }

    public int ZoomPercent
    {
        set => _zoom.Text = $"{value}%";
    }

    private FrameworkElement Build()
    {
        _warning.FontSize = 11;
        _warning.Foreground = _theme.Muted;
        _warning.TextWrapping = TextWrapping.Wrap;
        _zoom.Width = 48;
        _zoom.TextAlignment = TextAlignment.Center;
        _zoom.FontSize = 11;
        _zoom.Foreground = _theme.Muted;

        var zoomOut = MakeButton("缩小", "\uE71F", () => ZoomOutRequested?.Invoke());
        var fit = MakeButton("适应窗口", "\uE9A6", () => FitRequested?.Invoke());
        var zoomIn = MakeButton("放大", "\uE8A3", () => ZoomInRequested?.Invoke());
        var copy = MakeButton("复制成品", "\uE8C8", () => CopyRequested?.Invoke());
        var export = MakeButton("导出 PNG", "\uE74E", () => ExportRequested?.Invoke());

        var row = new Grid
        {
            MinHeight = 52,
            Padding = new Thickness(14, 8, 14, 8),
            Background = _theme.Card,
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(zoomOut);
        Grid.SetColumn(fit, 1);
        row.Children.Add(fit);
        Grid.SetColumn(zoomIn, 2);
        row.Children.Add(zoomIn);
        Grid.SetColumn(_zoom, 3);
        row.Children.Add(_zoom);
        Grid.SetColumn(_warning, 4);
        row.Children.Add(_warning);
        Grid.SetColumn(copy, 5);
        row.Children.Add(copy);
        Grid.SetColumn(export, 6);
        row.Children.Add(export);
        return row;
    }

    private Button MakeButton(string title, string glyph, Action action)
    {
        var foreground = _theme.Text;
        var button = new Button
        {
            MinHeight = 34,
            Padding = new Thickness(10, 5, 10, 5),
            Background = _theme.Panel,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = glyph,
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        FontSize = 14,
                        Foreground = foreground
                    },
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 12,
                        Foreground = foreground,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };
        button.Click += (_, _) => action();
        return button;
    }
}
