using Index.Annotation;
using Index.Storage;
using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Editor;

/// <summary>Owns the editor viewport, pixel-sized canvas and zoom behavior.</summary>
internal sealed class EditorCanvasPane : UserControl, IDisposable
{
    private readonly ShotRecord _shot;
    private readonly ScrollViewer _viewport;
    private bool _hasImage;
    private bool _disposed;

    public EditorCanvasPane(
        AnnotationState annotation,
        ShotRecord shot,
        GalleryTheme theme)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        _shot = shot ?? throw new ArgumentNullException(nameof(shot));
        ArgumentNullException.ThrowIfNull(theme);

        Canvas = new AnnotationCanvasView(annotation)
        {
            Width = Math.Max(1, shot.PixelWidth),
            Height = Math.Max(1, shot.PixelHeight),
            IsEnabled = false
        };
        _viewport = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 0.1f,
            MaxZoomFactor = 8f,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new Border
            {
                Background = theme.ThumbnailBackground,
                BorderBrush = theme.CardBorder,
                BorderThickness = new Thickness(1),
                Child = Canvas
            }
        };
        _viewport.ViewChanged += OnViewChanged;
        SizeChanged += OnSizeChanged;
        Content = _viewport;
    }

    public AnnotationCanvasView Canvas { get; }

    public int ZoomPercent => (int)Math.Round(_viewport.ZoomFactor * 100);

    public event Action<int>? ZoomChanged;

    public void SetImage(byte[] png, bool canEdit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(png);
        Canvas.SetBackground(png);
        Canvas.IsEnabled = canEdit;
        _hasImage = true;
        Fit();
    }

    public void Fit()
    {
        if (!_hasImage || _viewport.ActualWidth <= 1 || _viewport.ActualHeight <= 1)
            return;
        double factor = Math.Min(
            Math.Max(1, _viewport.ActualWidth - 48) / Math.Max(1, _shot.PixelWidth),
            Math.Max(1, _viewport.ActualHeight - 48) / Math.Max(1, _shot.PixelHeight));
        float zoom = (float)Math.Clamp(
            factor,
            _viewport.MinZoomFactor,
            _viewport.MaxZoomFactor);
        _viewport.ChangeView(null, null, zoom, disableAnimation: true);
    }

    public void ZoomBy(float multiplier)
    {
        if (!_hasImage)
            return;
        float zoom = Math.Clamp(
            _viewport.ZoomFactor * multiplier,
            _viewport.MinZoomFactor,
            _viewport.MaxZoomFactor);
        _viewport.ChangeView(null, null, zoom, disableAnimation: false);
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        => ZoomChanged?.Invoke(ZoomPercent);

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_hasImage)
            Fit();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _viewport.ViewChanged -= OnViewChanged;
        SizeChanged -= OnSizeChanged;
        Canvas.Dispose();
    }
}
