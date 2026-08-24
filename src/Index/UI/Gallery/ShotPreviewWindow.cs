using Index.Platform;
using Index.Storage;
using Index.UI.Molecule;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;

namespace Index.UI.Gallery;

/// <summary>A single reusable original-image preview with in-app zoom and pan.</summary>
internal sealed class ShotPreviewWindow : Window
{
    private const double MinScale = 0.2;
    private const double MaxScale = 8.0;
    private const double WheelScale = 1.08;

    private readonly ShotStore _store;
    private readonly GalleryTheme _theme;
    private readonly MolGrapherClient _molGrapher = new();
    private readonly Grid _root;
    private readonly Grid _viewport;
    private readonly Grid _imageLayer;
    private readonly Image _image;
    private readonly CompositeTransform _imageTransform = new();
    private readonly TextBlock _title;
    private readonly TextBlock _metadata;
    private readonly TextBlock _zoomLabel;
    private readonly RectangleGeometry _viewportClip = new();
    private ShotRecord _shot;
    private bool _isPanning;
    private uint _panPointerId;
    private Point _lastPointerPosition;
    private Button? _recognizeButton;

    public ShotPreviewWindow(ShotStore store, GalleryTheme theme, ShotRecord shot)
    {
        _store = store;
        _theme = theme;
        _shot = shot;
        AppWindow.Title = "Index · 大图预览";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Index.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        _root = new Grid
        {
            Background = theme.WindowBackground,
            Padding = new Thickness(18),
            IsTabStop = true
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var previous = MakeButton("←", "上一张");
        previous.Click += (_, _) => NavigationRequested?.Invoke(-1);
        toolbar.Children.Add(previous);

        var next = MakeButton("→", "下一张");
        next.Click += (_, _) => NavigationRequested?.Invoke(1);
        Grid.SetColumn(next, 1);
        toolbar.Children.Add(next);

        _title = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_title, 2);
        toolbar.Children.Add(_title);

        _zoomLabel = new TextBlock
        {
            Text = "100%",
            Foreground = theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(_zoomLabel, 3);
        toolbar.Children.Add(_zoomLabel);

        var open = MakeButton("打开原图", "使用系统默认程序打开");
        open.Click += (_, _) => OpenOriginal();
        Grid.SetColumn(open, 4);
        toolbar.Children.Add(open);

        _recognizeButton = MakeButton("识别分子", "调用 MolGrapher 识别截图中的化学分子");
        _recognizeButton.Click += async (_, _) => await RecognizeMoleculeAsync();
        Grid.SetColumn(_recognizeButton, 5);
        toolbar.Children.Add(_recognizeButton);

        _root.Children.Add(toolbar);

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _imageLayer = new Grid
        {
            RenderTransform = _imageTransform,
            RenderTransformOrigin = new Point(0, 0)
        };
        _imageLayer.Children.Add(_image);

        _viewport = new Grid
        {
            Background = theme.ThumbnailBackground,
            Clip = _viewportClip
        };
        _viewport.Children.Add(_imageLayer);
        _viewport.SizeChanged += OnViewportSizeChanged;
        _viewport.PointerWheelChanged += OnPointerWheelChanged;
        _viewport.PointerPressed += OnPointerPressed;
        _viewport.PointerMoved += OnPointerMoved;
        _viewport.PointerReleased += OnPointerReleased;
        _viewport.PointerCanceled += OnPointerReleased;
        _viewport.DoubleTapped += (_, _) => ResetView();

        var imageHost = new Border
        {
            Background = theme.ThumbnailBackground,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Child = _viewport
        };
        Grid.SetRow(imageHost, 1);
        _root.Children.Add(imageHost);

        _metadata = new TextBlock
        {
            Foreground = theme.Muted,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(_metadata, 2);
        _root.Children.Add(_metadata);

        _root.KeyDown += OnKeyDown;
        Content = _root;
        ShowShot(shot);
        Activated += (_, _) => _root.Focus(FocusState.Programmatic);
    }

    public event Action<int>? NavigationRequested;

    public void ShowShot(ShotRecord shot)
    {
        _shot = shot;
        _title.Text = shot.WindowTitle ?? shot.AppName ?? $"截图 {shot.Id}";
        _metadata.Text = $"{shot.PixelWidth} × {shot.PixelHeight}  ·  {shot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  ·  {shot.AppName ?? "未知来源"}";
        _image.Source = new BitmapImage(new Uri(_store.OriginalPath(shot)));
        ResetView();
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _imageLayer.Width = e.NewSize.Width;
        _imageLayer.Height = e.NewSize.Height;
        _viewportClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        UpdateTransformCenter();
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_viewport);
        double oldScale = _imageTransform.ScaleX == 0 ? 1 : _imageTransform.ScaleX;
        double factor = point.Properties.MouseWheelDelta > 0 ? WheelScale : 1 / WheelScale;
        double newScale = Math.Clamp(oldScale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 0.0001)
            return;

        double centerX = _imageTransform.CenterX;
        double centerY = _imageTransform.CenterY;
        double ratio = newScale / oldScale;
        _imageTransform.TranslateX = point.Position.X - centerX
            - (point.Position.X - centerX - _imageTransform.TranslateX) * ratio;
        _imageTransform.TranslateY = point.Position.Y - centerY
            - (point.Position.Y - centerY - _imageTransform.TranslateY) * ratio;
        _imageTransform.ScaleX = newScale;
        _imageTransform.ScaleY = newScale;
        UpdateZoomLabel();
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_viewport);
        if (!point.Properties.IsLeftButtonPressed || _imageTransform.ScaleX <= 1.01)
            return;

        _isPanning = true;
        _panPointerId = point.PointerId;
        _lastPointerPosition = point.Position;
        _viewport.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning || e.Pointer.PointerId != _panPointerId)
            return;

        var position = e.GetCurrentPoint(_viewport).Position;
        _imageTransform.TranslateX += position.X - _lastPointerPosition.X;
        _imageTransform.TranslateY += position.Y - _lastPointerPosition.Y;
        _lastPointerPosition = position;
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning || e.Pointer.PointerId != _panPointerId)
            return;

        _isPanning = false;
        _viewport.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ResetView()
    {
        _isPanning = false;
        _imageTransform.ScaleX = 1;
        _imageTransform.ScaleY = 1;
        _imageTransform.TranslateX = 0;
        _imageTransform.TranslateY = 0;
        UpdateTransformCenter();
        UpdateZoomLabel();
    }

    private void UpdateTransformCenter()
    {
        _imageTransform.CenterX = _viewport.ActualWidth / 2;
        _imageTransform.CenterY = _viewport.ActualHeight / 2;
    }

    private void UpdateZoomLabel()
        => _zoomLabel.Text = $"{_imageTransform.ScaleX * 100:0}%";

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                NavigationRequested?.Invoke(-1);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                NavigationRequested?.Invoke(1);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void OpenOriginal()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.OriginalPath(_shot))
        {
            UseShellExecute = true
        });
    }

    private async Task RecognizeMoleculeAsync()
    {
        if (_recognizeButton is not { } btn) return;
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        btn.IsEnabled = false;
        var originalText = (string)btn.Content;
        btn.Content = "识别中…";
        try
        {
            var pngPath = _store.OriginalPath(_shot);
            var pngData = await File.ReadAllBytesAsync(pngPath);
            var result = await _molGrapher.RecognizeAsync(pngData);

            var tcs = new TaskCompletionSource<bool>();
            dispatcher.TryEnqueue(() =>
            {
                try
                {
                    if (result.Error is not null)
                    {
                        _metadata.Text = $"分子识别失败：{result.Error}";
                    }
                    else if (result.Smiles is null)
                    {
                        _metadata.Text = "未检测到分子结构";
                    }
                    else
                    {
                        var window = new MoleculePreviewWindow(
                            _theme,
                            result.Smiles,
                            result.Sdf,
                            result.Confidence,
                            result.ProcessingTimeMs);
                        window.Activate();
                    }
                }
                finally
                {
                    tcs.TrySetResult(true);
                }
            });
            await tcs.Task;
        }
        catch (Exception ex)
        {
            var tcs = new TaskCompletionSource<bool>();
            dispatcher.TryEnqueue(() =>
            {
                _metadata.Text = $"分子识别异常：{ex.Message}";
                tcs.TrySetResult(true);
            });
            await tcs.Task;
        }
        finally
        {
            var tcs = new TaskCompletionSource<bool>();
            dispatcher.TryEnqueue(() =>
            {
                btn.IsEnabled = true;
                btn.Content = originalText;
                tcs.TrySetResult(true);
            });
            await tcs.Task;
        }
    }

    private Button MakeButton(string text, string tooltip)
    {
        var button = new Button
        {
            Content = text,
            Foreground = _theme.Text,
            Padding = new Thickness(14, 8, 14, 8)
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}
