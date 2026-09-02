using Index.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;

namespace Index.UI.Gallery;

/// <summary>A recyclable thumbnail surface backed only by the shot-asset contract.</summary>
internal sealed class ShotAssetThumbnailView : UserControl, IDisposable
{
    private readonly IShotAssetReader _assets;
    private readonly SKXamlCanvas _canvas;
    private CancellationTokenSource? _cancellation;
    private ShotRecord _shot;
    private SKBitmap? _bitmap;
    private string? _message;
    private bool _disposed;
    private bool _loadingEnabled = true;
    private int _generation;
    private Task _loadTask = Task.CompletedTask;

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

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loadingEnabled)
            BeginLoad();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseImage();

    private void BeginLoad()
    {
        if (_disposed || !_loadingEnabled || _cancellation is not null)
            return;

        _loadTask = ObserveLoadAsync();
    }

    private async Task ObserveLoadAsync()
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail load failed: {error.Message}");
        }
    }

    private async Task LoadAsync()
    {
        var generation = _generation;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var dispatcher = DispatcherQueue;
        try
        {
            var asset = await _assets.ReadPreviewAsync(_shot, cancellation.Token);
            if (!asset.HasData)
            {
                _ = dispatcher.TryEnqueue(() => ApplyMessage(
                    asset.Warning ?? StatusMessage(asset.Status),
                    cancellation,
                    generation));
                return;
            }

            var bytes = asset.Data.ToArray();
            var bitmap = await Task.Run(() => SKBitmap.Decode(bytes), cancellation.Token);
            if (bitmap is null)
            {
                _ = dispatcher.TryEnqueue(() => ApplyMessage(
                    "图片文件损坏",
                    cancellation,
                    generation));
                return;
            }
            if (!dispatcher.TryEnqueue(() => ApplyBitmap(bitmap, asset.Warning, cancellation, generation)))
                bitmap.Dispose();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _ = dispatcher.TryEnqueue(() => ApplyMessage(error.Message, cancellation, generation));
        }
    }

    private void ApplyBitmap(
        SKBitmap bitmap,
        string? warning,
        CancellationTokenSource cancellation,
        int generation)
    {
        if (!IsCurrent(cancellation, generation))
        {
            bitmap.Dispose();
            return;
        }

        _bitmap?.Dispose();
        _bitmap = bitmap;
        _message = null;
        ToolTipService.SetToolTip(this, warning);
        _canvas.Invalidate();
    }

    private void ApplyMessage(
        string message,
        CancellationTokenSource cancellation,
        int generation)
    {
        if (!IsCurrent(cancellation, generation))
            return;
        _message = message;
        ToolTipService.SetToolTip(this, message);
        _canvas.Invalidate();
    }

    private bool IsCurrent(CancellationTokenSource cancellation, int generation)
        => !_disposed
           && !cancellation.IsCancellationRequested
           && generation == _generation
           && ReferenceEquals(_cancellation, cancellation);

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

    public void Bind(ShotRecord shot)
    {
        ArgumentNullException.ThrowIfNull(shot);
        if (_shot.Id == shot.Id && string.Equals(_shot.Sha256, shot.Sha256, StringComparison.Ordinal))
            return;

        _shot = shot;
        _generation++;
        ReleaseImage();
        _message = null;
        ToolTipService.SetToolTip(this, null);
        _canvas.Invalidate();
        if (_loadingEnabled && IsLoaded)
            BeginLoad();
    }

    public void SetLoadingEnabled(bool enabled)
    {
        if (_disposed || _loadingEnabled == enabled)
            return;
        _loadingEnabled = enabled;
        if (!enabled)
        {
            _generation++;
            ReleaseImage();
            return;
        }
        if (IsLoaded)
            BeginLoad();
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
        if (_disposed)
            return;
        _disposed = true;
        _generation++;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _canvas.PaintSurface -= Paint;
        ReleaseImage();
    }

    private static string StatusMessage(ShotAssetStatus status) => status switch
    {
        ShotAssetStatus.Corrupt => "图片文件损坏",
        _ => "图片文件不存在"
    };
}
