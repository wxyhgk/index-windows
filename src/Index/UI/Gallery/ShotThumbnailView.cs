using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace Index.UI.Gallery;

/// <summary>用 Skia 绘制后台缓存管线提供的缩略图。</summary>
internal sealed class ShotThumbnailView : UserControl, IDisposable
{
    private readonly SKXamlCanvas _canvas;
    private string[] _paths;
    private ThumbnailLease? _lease;
    private CancellationTokenSource? _loadCancellation;
    private string? _error;
    private bool _disposed;
    private bool _loadingEnabled = true;

    public ShotThumbnailView(params string[] paths)
    {
        _paths = paths;
        _canvas = new SKXamlCanvas
        {
            Width = 320,
            Height = 180,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IgnorePixelScaling = true
        };
        _canvas.PaintSurface += OnPaintSurface;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = _canvas
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadingEnabled)
            StartLoad();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => ReleaseViewResources();

    private void StartLoad() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_disposed || _lease is not null) return;
        ReleaseViewResources();
        var dispatcher = DispatcherQueue;
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        try
        {
            var lease = await ThumbnailLoader.Shared.LoadFirstAsync(_paths, cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested)
            {
                lease.Dispose();
                return;
            }

            if (!dispatcher.TryEnqueue(() => ApplyLoadedLease(lease, cancellation)))
                lease.Dispose();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (_disposed || cancellation.IsCancellationRequested) return;
            var message = error.Message;
            _ = dispatcher.TryEnqueue(() => ApplyLoadError(message, cancellation));
        }
    }

    private void ApplyLoadedLease(ThumbnailLease lease, CancellationTokenSource cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_loadCancellation, cancellation))
        {
            lease.Dispose();
            return;
        }

        _lease?.Dispose();
        _lease = lease;
        _error = null;
        ToolTipService.SetToolTip(this, null);
        _canvas.Invalidate();
    }

    private void ApplyLoadError(string message, CancellationTokenSource cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_loadCancellation, cancellation))
            return;
        _error = message;
        ToolTipService.SetToolTip(this, $"图片加载失败：{message}");
        _canvas.Invalidate();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_lease is null)
        {
            if (_error is null) return;
            using var errorPaint = new SKPaint
            {
                Color = new SKColor(0xF0, 0x78, 0x78),
                IsAntialias = true
            };
            using var errorFont = new SKFont(SKTypeface.Default, 14);
            canvas.DrawText("加载失败", 12, 24, SKTextAlign.Left, errorFont, errorPaint);
            return;
        }

        var bitmap = _lease.Bitmap;
        var scale = Math.Min(
            e.Info.Width / (float)bitmap.Width,
            e.Info.Height / (float)bitmap.Height);
        var drawWidth = bitmap.Width * scale;
        var drawHeight = bitmap.Height * scale;
        var destination = new SKRect(
            (e.Info.Width - drawWidth) / 2,
            (e.Info.Height - drawHeight) / 2,
            (e.Info.Width + drawWidth) / 2,
            (e.Info.Height + drawHeight) / 2);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(bitmap, destination, paint);
    }

    private void ReleaseViewResources()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _lease?.Dispose();
        _lease = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _canvas.PaintSurface -= OnPaintSurface;
        ReleaseViewResources();
    }

    public void SetLoadingEnabled(bool enabled)
    {
        if (_disposed || _loadingEnabled == enabled)
            return;
        _loadingEnabled = enabled;
        if (!enabled)
        {
            ReleaseViewResources();
            return;
        }
        if (IsLoaded)
            StartLoad();
    }

    public void SetPaths(params string[] paths)
    {
        if (_paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
            return;
        ReleaseViewResources();
        _paths = paths;
        _error = null;
        _canvas.Invalidate();
        if (_loadingEnabled && IsLoaded)
            StartLoad();
    }
}
