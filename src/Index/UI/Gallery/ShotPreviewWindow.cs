using System.Runtime.InteropServices.WindowsRuntime;
using Index.Gallery;
using Index.Storage;
using Index.UI.Molecule;
using Index.Molecule;
using Index.Preview;
using Index.Recognition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;

namespace Index.UI.Gallery;

/// <summary>An embedded image and molecule workspace with in-app zoom and pan.</summary>
internal sealed class ShotPreviewView : UserControl, IDisposable
{
    private const double MinScale = 0.2;
    private const double MaxScale = 8.0;
    private const double WheelScale = 1.08;

    private readonly IShotAssetReader _shotAssets;
    private readonly GalleryShotCommandService _commands;
    private readonly ShotRecognitionWorkflow<MoleculeRecognitionResult> _moleculeRecognition;
    private readonly PreviewSessionController _previewSessions = new();
    private readonly GalleryTheme _theme;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly Grid _root;
    private readonly Grid _workspace;
    private readonly Grid _viewport;
    private readonly Grid _imageLayer;
    private readonly Image _image;
    private readonly CompositeTransform _imageTransform = new();
    private readonly TextBlock _title;
    private readonly TextBlock _metadata;
    private readonly TextBlock _zoomLabel;
    private readonly MoleculeWorkspacePresenter _moleculePresenter;
    private readonly RectangleGeometry _viewportClip = new();
    private ShotRecord _shot;
    private bool _isPanning;
    private uint _panPointerId;
    private Point _lastPointerPosition;
    private Button? _recognizeButton;
    private PreviewSession? _session;
    private bool _disposed;

    public ShotPreviewView(
        IShotAssetReader shotAssets,
        GalleryShotCommandService commands,
        IRecognitionPluginRegistry recognitionPlugins,
        GalleryTheme theme,
        ShotRecord shot)
    {
        _shotAssets = shotAssets ?? throw new ArgumentNullException(nameof(shotAssets));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _moleculeRecognition = new ShotRecognitionWorkflow<MoleculeRecognitionResult>(
            shotAssets,
            recognitionPlugins,
            RecognitionCapabilities.MoleculeStructure,
            new MoleculeRecognitionOutputPolicy());
        _theme = theme;
        _shot = shot;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
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
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = MakeButton("返回图库", "关闭工作区并返回图库");
        back.Click += (_, _) => CloseRequested?.Invoke();
        toolbar.Children.Add(back);

        var previous = MakeButton("←", "上一张");
        previous.Click += (_, _) => NavigationRequested?.Invoke(-1);
        Grid.SetColumn(previous, 1);
        toolbar.Children.Add(previous);

        var next = MakeButton("→", "下一张");
        next.Click += (_, _) => NavigationRequested?.Invoke(1);
        Grid.SetColumn(next, 2);
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
        Grid.SetColumn(_title, 3);
        toolbar.Children.Add(_title);

        _zoomLabel = new TextBlock
        {
            Text = "100%",
            Foreground = theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 48,
            TextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(_zoomLabel, 4);
        toolbar.Children.Add(_zoomLabel);

        var open = MakeButton("打开原图", "使用系统默认程序打开");
        open.Click += OnOpenOriginalClicked;
        Grid.SetColumn(open, 5);
        toolbar.Children.Add(open);

        _recognizeButton = MakeButton("识别分子", "调用 MolGrapher 识别截图中的化学分子");
        _recognizeButton.Click += OnRecognizeClicked;
        Grid.SetColumn(_recognizeButton, 6);
        toolbar.Children.Add(_recognizeButton);

        var drawMolecule = MakeButton("绘制分子", "打开空白 Ketcher 分子编辑器");
        Grid.SetColumn(drawMolecule, 7);
        toolbar.Children.Add(drawMolecule);

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

        _workspace = new Grid { ColumnSpacing = 12 };
        _workspace.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(0.8, GridUnitType.Star)
        });
        var moleculeColumn = new ColumnDefinition { Width = new GridLength(0) };
        _workspace.ColumnDefinitions.Add(moleculeColumn);
        _workspace.Children.Add(imageHost);

        var moleculeLayout = new Grid();
        moleculeLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        moleculeLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        moleculeLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var moleculeHeader = new Grid { Margin = new Thickness(12, 10, 8, 8) };
        moleculeHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        moleculeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        moleculeHeader.Children.Add(new TextBlock
        {
            Text = "分子结构",
            Foreground = theme.Text,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var closeMolecule = MakeButton("收起", "隐藏分子编辑区，保留已加载的编辑器");
        Grid.SetColumn(closeMolecule, 1);
        moleculeHeader.Children.Add(closeMolecule);
        moleculeLayout.Children.Add(moleculeHeader);

        var moleculeHost = new Grid();
        Grid.SetRow(moleculeHost, 1);
        moleculeLayout.Children.Add(moleculeHost);

        var moleculeFooter = new StackPanel
        {
            Margin = new Thickness(12, 8, 12, 10),
            Spacing = 2
        };
        var moleculeStatus = new TextBlock
        {
            Text = "编辑器尚未打开",
            Foreground = theme.Text,
            FontSize = 12
        };
        var moleculeDetails = new TextBlock
        {
            Foreground = theme.Muted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        moleculeFooter.Children.Add(moleculeStatus);
        moleculeFooter.Children.Add(moleculeDetails);
        Grid.SetRow(moleculeFooter, 2);
        moleculeLayout.Children.Add(moleculeFooter);

        var moleculePanel = new Border
        {
            Background = theme.ThumbnailBackground,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = moleculeLayout,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(moleculePanel, 1);
        _workspace.Children.Add(moleculePanel);

        _moleculePresenter = new MoleculeWorkspacePresenter(
            moleculeHost,
            moleculePanel,
            moleculeColumn,
            moleculeStatus,
            moleculeDetails,
            ReturnToUiAsync);
        closeMolecule.Click += (_, _) => _moleculePresenter.Hide();
        drawMolecule.Click += OnDrawMoleculeClicked;

        Grid.SetRow(_workspace, 1);
        _root.Children.Add(_workspace);

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
        Loaded += (_, _) => _root.Focus(FocusState.Programmatic);
    }

    public event Action<int>? NavigationRequested;
    public event Action? CloseRequested;

    public void ShowShot(ShotRecord shot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _moleculeRecognition.CancelCurrent();
        var session = _previewSessions.Begin(shot.Id);
        _session = session;
        _shot = shot;
        _title.Text = shot.WindowTitle ?? shot.AppName ?? $"截图 {shot.Id}";
        _metadata.Text = $"{shot.PixelWidth} × {shot.PixelHeight}  ·  {shot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  ·  {shot.AppName ?? "未知来源"}";
        _image.Source = null;
        _recognizeButton!.IsEnabled = true;
        _recognizeButton.Content = "识别分子";
        _ = LoadImageAsync(shot, session);
        _moleculePresenter.Hide();
        ResetView();
    }

    private async Task LoadImageAsync(
        ShotRecord shot,
        PreviewSession session)
    {
        var cancellationToken = session.CancellationToken;
        try
        {
            var asset = await _shotAssets.ReadRenderedAsync(shot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await ReturnToUiAsync();
            if (!_previewSessions.IsCurrent(session))
                return;

            if (!asset.HasData)
            {
                _metadata.Text = $"图片加载失败：{asset.Warning ?? "图片文件不存在"}";
                return;
            }

            var bitmap = new BitmapImage();
            using var memory = new MemoryStream(asset.Data.ToArray(), writable: false);
            using var stream = memory.AsRandomAccessStream();
            bitmap.SetSource(stream);
            _image.Source = bitmap;
            if (asset.Status == ShotAssetStatus.ThumbnailFallback)
                _metadata.Text += $"  ·  {asset.Warning}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            try
            {
                await ReturnToUiAsync();
            }
            catch
            {
                return;
            }
            if (_previewSessions.IsCurrent(session))
                _metadata.Text = $"图片加载失败：{ex.Message}";
        }
    }

    private Task ReturnToUiAsync()
    {
        if (_dispatcher.HasThreadAccess)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>();
        if (!_dispatcher.TryEnqueue(() => completion.TrySetResult(true)))
            completion.TrySetException(new InvalidOperationException("无法切换到 UI 线程"));
        return completion.Task;
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
                CloseRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private async void OnOpenOriginalClicked(object sender, RoutedEventArgs args)
    {
        var session = _session;
        if (session is null)
            return;

        try
        {
            await _commands.OpenOriginalAsync(_shot, session.CancellationToken);
        }
        catch (OperationCanceledException) when (session.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (_previewSessions.IsCurrent(session))
                _metadata.Text = $"打开原图失败：{error.Message}";
        }
    }

    private async void OnRecognizeClicked(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
            return;

        try
        {
            await RecognizeMoleculeAsync(session);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (_previewSessions.IsCurrent(session))
                _metadata.Text = $"分子识别异常：{error.Message}";
        }
    }

    private async void OnDrawMoleculeClicked(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
            return;

        try
        {
            await _moleculePresenter.OpenBlankAsync(
                session.CancellationToken,
                () => _previewSessions.IsCurrent(session));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (_previewSessions.IsCurrent(session))
                _metadata.Text = $"分子编辑器异常：{error.Message}";
        }
    }

    private async Task RecognizeMoleculeAsync(PreviewSession session)
    {
        if (_recognizeButton is not { } btn) return;
        if (!_previewSessions.IsCurrent(session)) return;
        var cancellationToken = session.CancellationToken;
        var shot = _shot;
        btn.IsEnabled = false;
        var originalText = (string)btn.Content;
        btn.Content = "识别中…";
        try
        {
            var run = await _moleculeRecognition.RunAsync(shot, cancellationToken);
            if (run.Status == RecognitionWorkflowStatus.Cancelled)
                return;

            await RunOnUiAsync(async () =>
            {
                if (!_previewSessions.IsCurrent(session))
                    return;

                if (!string.IsNullOrWhiteSpace(run.Warning))
                    _metadata.Text = $"{run.Warning}；识别精度可能降低";

                if (run.Status == RecognitionWorkflowStatus.Error)
                {
                    _metadata.Text = $"分子识别失败：{run.Error ?? "未知错误"}";
                }
                else if (run.Status == RecognitionWorkflowStatus.Empty)
                {
                    _metadata.Text = "未检测到分子结构";
                }
                else if (run is
                    {
                        Status: RecognitionWorkflowStatus.Success,
                        Output: { } result
                    })
                {
                    await _moleculePresenter.PresentAsync(
                        result,
                        cancellationToken,
                        () => _previewSessions.IsCurrent(session));
                    if (!_previewSessions.IsCurrent(session))
                        return;
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (_previewSessions.IsCurrent(session))
                    _metadata.Text = $"分子识别异常：{ex.Message}";
            });
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                if (_previewSessions.IsCurrent(session))
                {
                    btn.IsEnabled = true;
                    btn.Content = originalText;
                }
            });
        }
    }

    private Task RunOnUiAsync(Action action)
        => RunOnUiAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (_dispatcher.HasThreadAccess)
            return action();

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            completion.TrySetCanceled();
        }
        return completion.Task;
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

    public void Deactivate()
    {
        if (_disposed)
            return;

        _moleculeRecognition.CancelCurrent();
        _previewSessions.Deactivate();
        _session = null;
        _recognizeButton!.IsEnabled = true;
        _recognizeButton.Content = "识别分子";
        _moleculePresenter.Hide();
        _image.Source = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Deactivate();
        _disposed = true;
        _moleculeRecognition.Dispose();
        _previewSessions.Dispose();
        _moleculePresenter.Dispose();
    }
}
