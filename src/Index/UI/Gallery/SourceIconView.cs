using Index.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace Index.UI.Gallery;

/// <summary>网页 favicon 覆盖在浏览器图标上；站点读取失败时自然露出浏览器图标。</summary>
internal sealed class SourceIconView : Grid, IDisposable
{
    private readonly AppIconView _applicationIcon;
    private readonly SKXamlCanvas _faviconCanvas;
    private readonly string? _sourceUrl;
    private CancellationTokenSource? _cancellation;
    private SKBitmap? _favicon;
    private bool _disposed;

    public SourceIconView(string? executablePath, string? appName, string? sourceUrl)
    {
        _sourceUrl = sourceUrl;
        Width = 14;
        Height = 14;
        _applicationIcon = new AppIconView(executablePath, appName);
        Children.Add(_applicationIcon);
        _faviconCanvas = new SKXamlCanvas { IgnorePixelScaling = true };
        _faviconCanvas.PaintSurface += Paint;
        Children.Add(_faviconCanvas);
        ToolTipService.SetToolTip(this, sourceUrl ?? appName);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(_sourceUrl)) BeginLoad();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseFavicon();

    private async void BeginLoad()
    {
        if (_disposed || _cancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var dispatcher = DispatcherQueue;
        try
        {
            var bytes = await WebsiteFaviconProvider.Shared.GetAsync(_sourceUrl);
            if (bytes is null || cancellation.IsCancellationRequested || _disposed) return;
            var bitmap = await Task.Run(() => SKBitmap.Decode(bytes), cancellation.Token);
            if (bitmap is null) return;
            if (!dispatcher.TryEnqueue(() => Apply(bitmap, cancellation)))
                bitmap.Dispose();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private void Apply(SKBitmap bitmap, CancellationTokenSource cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested
            || !ReferenceEquals(_cancellation, cancellation))
        {
            bitmap.Dispose();
            return;
        }
        _favicon?.Dispose();
        _favicon = bitmap;
        _faviconCanvas.Invalidate();
    }

    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        args.Surface.Canvas.Clear(SKColors.Transparent);
        if (_favicon is null) return;
        using var paint = new SKPaint { IsAntialias = true };
        args.Surface.Canvas.DrawBitmap(
            _favicon,
            new SKRect(0, 0, args.Info.Width, args.Info.Height),
            paint);
    }

    private void ReleaseFavicon()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _favicon?.Dispose();
        _favicon = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _faviconCanvas.PaintSurface -= Paint;
        ReleaseFavicon();
        _applicationIcon.Dispose();
    }
}
