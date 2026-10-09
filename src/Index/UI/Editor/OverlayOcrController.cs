using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Platform.Clipboard;
using Index.Platform.Diagnostics;
using Index.Ocr;

namespace Index.UI.Editor;

/// <summary>
/// 覆盖层 OCR 模式控制器：管理文字识别的激活、停用、复制和异步生命周期。
/// 从 OverlayWindow 拆出，减少主窗口的 boolean 状态字段和异步复杂度。
/// </summary>
internal sealed class OverlayOcrController
{
    private readonly IOcrTextRecognizer _ocrTextRecognizer;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly IAppDiagnostics _diagnostics;
    private readonly OcrTextOverlayView _liveTextOverlay;
    private readonly AnnotationCanvasView _annotationCanvas;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeLabelText;
    private readonly AnnotationState _annotation;
    private readonly Action _queueToolbarRefresh;
    private readonly Func<DispatcherQueue> _getDispatcherQueue;
    private readonly Func<bool> _getHasClosed;
    private readonly Func<Rect> _getSelection;
    private readonly Func<byte[]> _getFrozenPng;
    private readonly Func<CaptureCoordinateMapper> _getCoordinates;
    private readonly Func<double> _getDpiScale;

    private CancellationTokenSource? _ocrCancellation;
    private int _ocrGeneration;
    private bool _isOcrModeActive;
    private bool _isOcrLoading;
    private bool _copyAllWhenOcrCompletes;

    public bool IsActive => _isOcrModeActive;
    public bool HasSelection => _liveTextOverlay.HasSelection;

    public OverlayOcrController(
        IOcrTextRecognizer ocrTextRecognizer,
        IClipboardWriter clipboardWriter,
        IAppDiagnostics diagnostics,
        OcrTextOverlayView liveTextOverlay,
        AnnotationCanvasView annotationCanvas,
        Border sizeLabel,
        TextBlock sizeLabelText,
        AnnotationState annotation,
        Action queueToolbarRefresh,
        Func<DispatcherQueue> getDispatcherQueue,
        Func<bool> getHasClosed,
        Func<Rect> getSelection,
        Func<byte[]> getFrozenPng,
        Func<CaptureCoordinateMapper> getCoordinates,
        Func<double> getDpiScale)
    {
        _ocrTextRecognizer = ocrTextRecognizer;
        _clipboardWriter = clipboardWriter;
        _diagnostics = diagnostics;
        _liveTextOverlay = liveTextOverlay;
        _annotationCanvas = annotationCanvas;
        _sizeLabel = sizeLabel;
        _sizeLabelText = sizeLabelText;
        _annotation = annotation;
        _queueToolbarRefresh = queueToolbarRefresh;
        _getDispatcherQueue = getDispatcherQueue;
        _getHasClosed = getHasClosed;
        _getSelection = getSelection;
        _getFrozenPng = getFrozenPng;
        _getCoordinates = getCoordinates;
        _getDpiScale = getDpiScale;

        _liveTextOverlay.SelectionChanged += OnLiveTextSelectionChanged;
    }

    public void Activate()
    {
        var selection = _getSelection();
        if (_isOcrModeActive || selection.IsEmpty || _getFrozenPng().Length == 0)
            return;

        _isOcrModeActive = true;
        _isOcrLoading = true;
        int generation = ++_ocrGeneration;
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _ocrCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        _liveTextOverlay.Clear();
        _annotationCanvas.IsHitTestVisible = false;
        _sizeLabelText.Text = "正在识别文字…";
        _sizeLabel.Visibility = Visibility.Visible;

        var coordinates = _getCoordinates();
        var crop = coordinates.ToCropRect(new SelectionRect(
            selection.X, selection.Y, selection.Width, selection.Height));
        WriteOcrDiagnostic(
            AppDiagnosticLevel.Trace,
            "recognition-started",
            new Dictionary<string, string?>
            {
                ["crop"] = $"{crop.X},{crop.Y},{crop.Width}x{crop.Height}",
                ["dpiScale"] = _getDpiScale().ToString(
                    "F3",
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        _ = RecognizeLiveTextAsync(
            generation,
            cancellation,
            new OcrPixelRect(crop.X, crop.Y, crop.Width, crop.Height));
        _queueToolbarRefresh();
    }

    public void Deactivate()
    {
        if (!_isOcrModeActive && !_isOcrLoading && _ocrCancellation is null)
            return;

        _isOcrModeActive = false;
        _isOcrLoading = false;
        _copyAllWhenOcrCompletes = false;
        _ocrGeneration++;
        var cancellation = Interlocked.Exchange(ref _ocrCancellation, null);
        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            cancellation.Dispose();
        }
        _liveTextOverlay.Clear();
        _annotationCanvas.IsHitTestVisible = !_isOcrModeActive
            && (_annotation.Tool.HasValue || !_annotation.IsEmpty);
        _queueToolbarRefresh();
    }

    public void CopySelected()
    {
        string text = _liveTextOverlay.SelectedText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _sizeLabelText.Text = "请先拖动选择文字";
            return;
        }

        try
        {
            _clipboardWriter.WriteText(text);
            _sizeLabelText.Text = $"已复制 {_liveTextOverlay.SelectedWordCount} 个词";
        }
        catch (Exception error)
        {
            _sizeLabelText.Text = "复制文字失败";
            WriteOcrDiagnostic(
                AppDiagnosticLevel.Error,
                "copy-failed",
                exception: error);
        }
    }

    public void CopyAll()
    {
        if (!_isOcrModeActive)
        {
            _copyAllWhenOcrCompletes = true;
            Activate();
            return;
        }

        if (_isOcrLoading)
        {
            _copyAllWhenOcrCompletes = true;
            _sizeLabelText.Text = "正在识别，完成后复制全部文字…";
            return;
        }

        _liveTextOverlay.SelectAll();
        CopySelected();
    }

    public void OnAnnotationStateChanged()
    {
        if (_annotation.Tool.HasValue && _isOcrModeActive)
            Deactivate();
        _annotationCanvas.IsHitTestVisible = !_isOcrModeActive
            && (_annotation.Tool.HasValue || !_annotation.IsEmpty);
    }

    public void OnClosed()
    {
        Deactivate();
        _liveTextOverlay.SelectionChanged -= OnLiveTextSelectionChanged;
    }

    public void ClearSelection() => _liveTextOverlay.ClearSelection();

    public void PositionOverlay(Rect selection)
    {
        Canvas.SetLeft(_liveTextOverlay, selection.X);
        Canvas.SetTop(_liveTextOverlay, selection.Y);
        _liveTextOverlay.Width = selection.Width;
        _liveTextOverlay.Height = selection.Height;
        _liveTextOverlay.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, selection.Width, selection.Height)
        };
    }

    private void OnLiveTextSelectionChanged()
    {
        if (!_isOcrModeActive || _isOcrLoading)
            return;

        _sizeLabelText.Text = _liveTextOverlay.HasSelection
            ? $"已选择 {_liveTextOverlay.SelectedWordCount} 个词 · Ctrl+C 复制"
            : "拖动选择文字 · Ctrl+C 复制";
    }

    private async Task RecognizeLiveTextAsync(
        int generation,
        CancellationTokenSource cancellation,
        OcrPixelRect crop)
    {
        try
        {
            var result = await _ocrTextRecognizer
                .RecognizeAsync(_getFrozenPng(), crop, cancellation.Token)
                .ConfigureAwait(false);
            WriteOcrDiagnostic(
                AppDiagnosticLevel.Trace,
                "recognition-completed",
                new Dictionary<string, string?>
                {
                    ["wordCount"] = result.Words.Count.ToString(),
                    ["dimensions"] = $"{result.PixelWidth}x{result.PixelHeight}",
                    ["language"] = result.LanguageTag
                });
            EnqueueLiveTextCompletion(generation, cancellation, () =>
            {
                if (result.Words.Count == 0)
                {
                    _sizeLabelText.Text = "没有识别到文字";
                    return;
                }

                var selection = _getSelection();
                PositionOverlay(selection);
                _liveTextOverlay.Bind(result, selection.Width, selection.Height);
                if (_copyAllWhenOcrCompletes)
                {
                    _copyAllWhenOcrCompletes = false;
                    _liveTextOverlay.SelectAll();
                    CopySelected();
                }
                else
                {
                    _sizeLabelText.Text = "拖动选择文字 · Ctrl+C 复制";
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            WriteOcrDiagnostic(
                AppDiagnosticLevel.Error,
                "recognition-failed",
                exception: error);
            EnqueueLiveTextCompletion(generation, cancellation, () =>
                _sizeLabelText.Text = "文字识别失败");
        }
    }

    private void EnqueueLiveTextCompletion(
        int generation,
        CancellationTokenSource cancellation,
        Action update)
    {
        void Apply()
        {
            if (_getHasClosed()
                || !_isOcrLoading
                || generation != _ocrGeneration
                || !ReferenceEquals(_ocrCancellation, cancellation)
                || cancellation.IsCancellationRequested)
            {
                ReleaseOcrCancellation(cancellation);
                return;
            }

            try
            {
                update();
            }
            catch (Exception error)
            {
                _sizeLabelText.Text = "复制文字失败";
                WriteOcrDiagnostic(
                    AppDiagnosticLevel.Error,
                    "copy-failed",
                    exception: error);
            }
            finally
            {
                _isOcrLoading = false;
                ReleaseOcrCancellation(cancellation);
            }
        }

        try
        {
            var dispatcherQueue = _getDispatcherQueue();
            if (dispatcherQueue.HasThreadAccess)
                Apply();
            else if (!dispatcherQueue.TryEnqueue(Apply))
                ReleaseOcrCancellation(cancellation);
        }
        catch
        {
            ReleaseOcrCancellation(cancellation);
        }
    }

    private void ReleaseOcrCancellation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(
            Interlocked.CompareExchange(ref _ocrCancellation, null, cancellation),
            cancellation))
        {
            cancellation.Dispose();
        }
    }

    private void WriteOcrDiagnostic(
        AppDiagnosticLevel level,
        string eventName,
        IReadOnlyDictionary<string, string?>? properties = null,
        Exception? exception = null)
    {
        try
        {
            _diagnostics.Write(
                level,
                "capture.ocr",
                eventName,
                properties,
                exception);
        }
        catch
        {
            // Diagnostics must never change capture/OCR behavior.
        }
    }
}
