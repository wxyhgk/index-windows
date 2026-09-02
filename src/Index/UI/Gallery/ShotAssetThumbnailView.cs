using Index.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace Index.UI.Gallery;

/// <summary>Thumbnail surface that reads through the shared shot-asset boundary.</summary>
internal sealed class ShotAssetThumbnailView : UserControl, IDisposable
{
    private readonly IShotAssetReader _assets;
    private readonly ShotRecord _shot;
    private readonly SKXamlCanvas _canvas;
    private CancellationTokenSource? _cancellation;
    private SKBitmap? _bitmap;
    private string? _message;
    private bool _disposed;

    public ShotAssetThumbnailView(IShotAssetReader assets, ShotRecord shot)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _shot = shot ?? throw new ArgumentNullException(nameof(shot));
        _canvas = new SKXamlCanvas
        {
            Width = 320,
            Height = 180,
            IgnorePixelScaling = true
        };
        _canvas.PaintSurface += Paint;
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

    private void OnLoaded(object sender, RoutedEventArgs args) => BeginLoad();

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseImage();

    private async void BeginLoad()
    {
        if (_disposed || _cancellation is not null)
            return;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var dispatcher = DispatcherQueue;
        try
        {
            var asset = await _assets.ReadPreviewAsync(_shot, cancellation.Token);
            if (!asset.HasData)
            {
                if (!dispatcher.TryEnqueue(() => ApplyMessage(asset.Warning ?? "图片不存在", cancellation)))
                    return;
                return;
            }

            var bytes = asset.Data.ToArray();
            var bitmap = await Task.Run(() => SKBitmap.Decode(bytes), cancellation.Token);
            if (bitmap is null)
            {
                _ = dispatcher.TryEnqueue(() => ApplyMessage("图片损坏", cancellation));
                return;
            }
            if (!dispatcher.TryEnqueue(() => ApplyBitmap(bitmap, cancellation)))
                bitmap.Dispose();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _ = dispatcher.TryEnqueue(() => ApplyMessage(error.Message, cancellation));
        }
    }

    private void ApplyBitmap(SKBitmap bitmap, CancellationTokenSource cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested
            || !ReferenceEquals(_cancellation, cancellation))
        {
            bitmap.Dispose();
            return;
        }

        _bitmap?.Dispose();
        _bitmap = bitmap;
        _message = null;
        _canvas.Invalidate();
    }

    private void ApplyMessage(string message, CancellationTokenSource cancellation)
    {
        if (_disposed || cancellation.IsCancellationRequested
            || !ReferenceEquals(_cancellation, cancellation))
            return;
        _message = message;
        ToolTipService.SetToolTip(this, message);
        _canvas.Invalidate();
    }

    private void Paint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var canvas = args.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_bitmap is { } bitmap)
        {
            var scale = Math.Min(
                args.Info.Width / (float)bitmap.Width,
                args.Info.Height / (float)bitmap.Height);
            var width = bitmap.Width * scale;
            var height = bitmap.Height * scale;
            var destination = new SKRect(
                (args.Info.Width - width) / 2,
                (args.Info.Height - height) / 2,
                (args.Info.Width + width) / 2,
                (args.Info.Height + height) / 2);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(bitmap, destination, paint);
            return;
        }

        if (_message is null)
            return;
        using var textPaint = new SKPaint
        {
            Color = new SKColor(0x9B, 0xA3, 0xB4),
            IsAntialias = true
        };
        using var font = new SKFont(SKTypeface.Default, 13);
        canvas.DrawText("无预览", 12, 24, SKTextAlign.Left, font, textPaint);
    }

    private void ReleaseImage()
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
        ReleaseImage();
    }
}
