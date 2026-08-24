using System.Runtime.InteropServices.WindowsRuntime;
using Index.Platform;
using Index.Storage;
using Index.UI.Molecule;
using Index.Molecule;
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

    private readonly ShotStore _store;
    private readonly IShotAssetReader _shotAssets;
    private readonly IRecognitionPluginRegistry _recognitionPlugins;
    private readonly GalleryTheme _theme;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly Grid _root;
    private readonly Grid _workspace;
    private readonly Grid _viewport;
    private readonly Grid _imageLayer;
    private readonly Grid _moleculeHost;
    private readonly Border _moleculePanel;
    private readonly ColumnDefinition _moleculeColumn;
    private readonly Image _image;
    private readonly CompositeTransform _imageTransform = new();
    private readonly TextBlock _title;
    private readonly TextBlock _metadata;
    private readonly TextBlock _moleculeStatus;
    private readonly TextBlock _moleculeDetails;
    private readonly TextBlock _zoomLabel;
    private readonly RectangleGeometry _viewportClip = new();
    private ShotRecord _shot;
    private bool _isPanning;
    private uint _panPointerId;
    private Point _lastPointerPosition;
    private Button? _recognizeButton;
    private MoleculeSketcherView? _moleculeSketcher;
    private Task? _moleculeInitialization;
    private int _moleculeLoadGeneration;
    private CancellationTokenSource _sessionCancellation = new();
    private Guid _sessionId;
    private bool _disposed;

    public ShotPreviewView(
        ShotStore store,
        IShotAssetReader shotAssets,
        IRecognitionPluginRegistry recognitionPlugins,
        GalleryTheme theme,
        ShotRecord shot)
    {
        _store = store;
        _shotAssets = shotAssets;
        _recognitionPlugins = recognitionPlugins;
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
        open.Click += (_, _) => OpenOriginal();
        Grid.SetColumn(open, 5);
        toolbar.Children.Add(open);

        _recognizeButton = MakeButton("识别分子", "调用 MolGrapher 识别截图中的化学分子");
        _recognizeButton.Click += async (_, _) => await RecognizeMoleculeAsync();
        Grid.SetColumn(_recognizeButton, 6);
        toolbar.Children.Add(_recognizeButton);

        var drawMolecule = MakeButton("绘制分子", "打开空白 Ketcher 分子编辑器");
        drawMolecule.Click += async (_, _) =>
            await ShowMoleculePaneAsync(null, null, 0, 0);
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
        _moleculeColumn = new ColumnDefinition { Width = new GridLength(0) };
        _workspace.ColumnDefinitions.Add(_moleculeColumn);
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
        closeMolecule.Click += (_, _) => HideMoleculePane();
        Grid.SetColumn(closeMolecule, 1);
        moleculeHeader.Children.Add(closeMolecule);
        moleculeLayout.Children.Add(moleculeHeader);

        _moleculeHost = new Grid();
        Grid.SetRow(_moleculeHost, 1);
        moleculeLayout.Children.Add(_moleculeHost);

        var moleculeFooter = new StackPanel
        {
            Margin = new Thickness(12, 8, 12, 10),
            Spacing = 2
        };
        _moleculeStatus = new TextBlock
        {
            Text = "编辑器尚未打开",
            Foreground = theme.Text,
            FontSize = 12
        };
        _moleculeDetails = new TextBlock
        {
            Foreground = theme.Muted,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        moleculeFooter.Children.Add(_moleculeStatus);
        moleculeFooter.Children.Add(_moleculeDetails);
        Grid.SetRow(moleculeFooter, 2);
        moleculeLayout.Children.Add(moleculeFooter);

        _moleculePanel = new Border
        {
            Background = theme.ThumbnailBackground,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = moleculeLayout,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(_moleculePanel, 1);
        _workspace.Children.Add(_moleculePanel);

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
        BeginSession();
        _shot = shot;
        _title.Text = shot.WindowTitle ?? shot.AppName ?? $"截图 {shot.Id}";
        _metadata.Text = $"{shot.PixelWidth} × {shot.PixelHeight}  ·  {shot.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  ·  {shot.AppName ?? "未知来源"}";
        _image.Source = null;
        _recognizeButton!.IsEnabled = true;
        _recognizeButton.Content = "识别分子";
        var sessionId = _sessionId;
        _ = LoadImageAsync(shot, sessionId, _sessionCancellation.Token);
        HideMoleculePane();
        ResetView();
    }

    private async Task LoadImageAsync(
        ShotRecord shot,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var asset = await _shotAssets.ReadBestAvailableAsync(shot, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await ReturnToUiAsync();
            if (!IsCurrentSession(sessionId))
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
            await ReturnToUiAsync();
            if (IsCurrentSession(sessionId))
                _metadata.Text = $"图片加载失败：{ex.Message}";
        }
    }

    private void BeginSession()
    {
        _sessionCancellation.Cancel();
        _sessionCancellation.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        _sessionId = Guid.NewGuid();
    }

    private bool IsCurrentSession(Guid sessionId)
        => !_disposed && sessionId == _sessionId;

    private async Task ShowMoleculePaneAsync(
        string? sdf,
        string? smiles,
        double confidence,
        int processingMs)
    {
        var generation = ++_moleculeLoadGeneration;
        await ReturnToUiAsync();

        _moleculePanel.Visibility = Visibility.Visible;
        _moleculeColumn.Width = new GridLength(1.2, GridUnitType.Star);
        _moleculeStatus.Text = "正在准备分子编辑器…";
        _moleculeDetails.Text = sdf is null && smiles is null
            ? "空白画布"
            : $"置信度 {confidence:P0}  ·  {processingMs}ms";
        if (_moleculeSketcher is null)
        {
            _moleculeSketcher = new MoleculeSketcherView();
            _moleculeHost.Children.Add(_moleculeSketcher);
            _moleculeInitialization = _moleculeSketcher.InitializeAsync();
        }

        try
        {
            await (_moleculeInitialization ?? Task.CompletedTask);
            await ReturnToUiAsync();
            if (generation != _moleculeLoadGeneration || _moleculeSketcher is null)
                return;

            if (!string.IsNullOrWhiteSpace(sdf))
                await _moleculeSketcher.SetSdfAsync(sdf);
            else if (!string.IsNullOrWhiteSpace(smiles))
                await _moleculeSketcher.SetSmilesAsync(smiles);
            else
                await _moleculeSketcher.ClearAsync();

            await ReturnToUiAsync();
            if (generation == _moleculeLoadGeneration)
                _moleculeStatus.Text = sdf is null && smiles is null
                    ? "空白画布已就绪"
                    : "识别结果已载入，可以对照原图编辑";
        }
        catch (Exception ex)
        {
            await ReturnToUiAsync();
            if (generation == _moleculeLoadGeneration)
                _moleculeStatus.Text = $"分子编辑器加载失败：{ex.Message}";
        }
    }

    private void HideMoleculePane()
    {
        _moleculeLoadGeneration++;
        _moleculePanel.Visibility = Visibility.Collapsed;
        _moleculeColumn.Width = new GridLength(0);
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

    private void OpenOriginal()
    {
        var path = _store.OriginalPath(_shot);
        if (!File.Exists(path))
        {
            _metadata.Text = "原图文件缺失，无法使用系统程序打开；工作区仍可显示缩略图";
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }

    private async Task RecognizeMoleculeAsync()
    {
        if (_recognizeButton is not { } btn) return;
        var sessionId = _sessionId;
        var cancellationToken = _sessionCancellation.Token;
        var shot = _shot;
        btn.IsEnabled = false;
        var originalText = (string)btn.Content;
        btn.Content = "识别中…";
        try
        {
            var asset = await _shotAssets.ReadBestAvailableAsync(shot, cancellationToken);
            if (!asset.HasData)
                throw new FileNotFoundException(asset.Warning ?? "图片文件不存在");
            if (asset.Status == ShotAssetStatus.ThumbnailFallback)
            {
                await RunOnUiAsync(() =>
                {
                    if (IsCurrentSession(sessionId))
                        _metadata.Text = $"{asset.Warning}；识别精度可能降低";
                });
            }

            var recognizer = _recognitionPlugins.GetRequired<MoleculeRecognitionResult>(
                RecognitionCapabilities.MoleculeStructure);
            var result = await recognizer.RecognizeAsync(
                RecognitionInput.Image(asset.Data),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await RunOnUiAsync(async () =>
            {
                if (!IsCurrentSession(sessionId))
                    return;

                if (result.Error is not null)
                {
                    _metadata.Text = $"分子识别失败：{result.Error}";
                }
                else if (string.IsNullOrWhiteSpace(result.Sdf)
                    && string.IsNullOrWhiteSpace(result.Smiles))
                {
                    _metadata.Text = "未检测到分子结构";
                }
                else
                {
                    await ShowMoleculePaneAsync(
                        result.Sdf,
                        result.Smiles,
                        result.Confidence,
                        result.ProcessingTimeMs);
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
                if (IsCurrentSession(sessionId))
                    _metadata.Text = $"分子识别异常：{ex.Message}";
            });
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                if (IsCurrentSession(sessionId))
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

        _sessionCancellation.Cancel();
        _sessionId = Guid.NewGuid();
        _recognizeButton!.IsEnabled = true;
        _recognizeButton.Content = "识别分子";
        HideMoleculePane();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Deactivate();
        _disposed = true;
        _sessionCancellation.Dispose();
        _moleculeLoadGeneration++;
        _moleculeSketcher?.Dispose();
        _moleculeSketcher = null;
        _moleculeInitialization = null;
    }
}
