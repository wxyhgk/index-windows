using Index.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace Index.UI.Gallery;

/// <summary>异步绘制来源应用图标；视图离屏时立即取消并释放位图。</summary>
internal sealed class AppIconView : UserControl, IDisposable
{
    private readonly string? _executablePath;
    private readonly SKXamlCanvas _canvas;
    private CancellationTokenSource? _cancellation;
    private SKBitmap? _bitmap;
    private bool _disposed;

    public AppIconView(string? executablePath, string? appName, double side = 14)
    {
        _executablePath = executablePath;
        Width = side;
        Height = side;
        ToolTipService.SetToolTip(this, appName);
        _canvas = new SKXamlCanvas { IgnorePixelScaling = true };
        _canvas.PaintSurface += Paint;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Content = _canvas;
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => StartLoad();

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseResources();

    private void StartLoad() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_disposed || _cancellation is not null) return;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var dispatcher = DispatcherQueue;
        try
        {
            var png = await WindowsAppIconProvider.Shared.GetIconPngAsync(_executablePath);
            if (png is null || cancellation.IsCancellationRequested || _disposed) return;
            var bitmap = await Task.Run(() => SKBitmap.Decode(png), cancellation.Token);
            if (bitmap is null) return;
            if (!dispatcher.TryEnqueue(() => ApplyBitmap(bitmap, cancellation)))
                bitmap.Dispose();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Application icon load failed: {error}");
        }
    }

    private void ApplyBitmap(SKBitmap bitmap, CancellationTokenSource cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested ||
            !ReferenceEquals(_cancellation, cancellation))
        {
            bitmap.Dispose();
            return;
        }
        _bitmap?.Dispose();
        _bitmap = bitmap;
        _canvas.Invalidate();
    }

    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        args.Surface.Canvas.Clear(SKColors.Transparent);
        if (_bitmap is null) return;
        using var paint = new SKPaint { IsAntialias = true };
        args.Surface.Canvas.DrawBitmap(
            _bitmap,
            new SKRect(0, 0, args.Info.Width, args.Info.Height),
            paint);
    }

    private void ReleaseResources()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _canvas.PaintSurface -= Paint;
        ReleaseResources();
    }
}
