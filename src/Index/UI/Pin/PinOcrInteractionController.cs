using Index.Ocr;
using Index.Pin;
using Index.Platform.Clipboard;
using Index.Platform.Diagnostics;
using Index.Platform.Windowing;
using Index.UI.Editor;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Index.UI.Pin;

/// <summary>
/// Owns one pin's automatic OCR lifecycle and text interaction. The pin remains visible while
/// recognition runs; all geometry is kept in source-image pixels and projected only at input or
/// presentation boundaries.
/// </summary>
internal sealed class PinOcrInteractionController : IDisposable
{
    private readonly PinWindowModel _model;
    private readonly IOcrTextRecognizer _recognizer;
    private readonly IClipboardWriter _clipboard;
    private readonly IAppDiagnostics _diagnostics;
    private readonly NativePinPresentation _nativePresentation;
    private readonly OcrTextOverlayView _fallbackOverlay;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<(double Width, double Height)> _fallbackSize;
    private readonly Action _enableFallbackClientInteraction;
    private readonly Action<int> _showSelectionStatus;
    private readonly Action _showDefaultStatus;
    private readonly Action _stateChanged;
    private readonly Action _dismiss;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<int> _renderedNativeSelection = [];
    private OcrTextResult? _result;
    private OcrTextSelection? _nativeSelection;
    private Task? _recognitionTask;
    private double _nativeDragStartX;
    private double _nativeDragStartY;
    private bool _isNativeTextDragging;
    private volatile bool _isRecognizing;
    private bool _disposed;

    public PinOcrInteractionController(
        PinWindowModel model,
        IOcrTextRecognizer recognizer,
        IClipboardWriter clipboard,
        IAppDiagnostics diagnostics,
        NativePinPresentation nativePresentation,
        OcrTextOverlayView fallbackOverlay,
        DispatcherQueue dispatcher,
        Func<(double Width, double Height)> fallbackSize,
        Action enableFallbackClientInteraction,
        Action<int> showSelectionStatus,
        Action showDefaultStatus,
        Action stateChanged,
        Action dismiss)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _nativePresentation = nativePresentation
            ?? throw new ArgumentNullException(nameof(nativePresentation));
        _fallbackOverlay = fallbackOverlay ?? throw new ArgumentNullException(nameof(fallbackOverlay));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fallbackSize = fallbackSize ?? throw new ArgumentNullException(nameof(fallbackSize));
        _enableFallbackClientInteraction = enableFallbackClientInteraction
            ?? throw new ArgumentNullException(nameof(enableFallbackClientInteraction));
        _showSelectionStatus = showSelectionStatus
            ?? throw new ArgumentNullException(nameof(showSelectionStatus));
        _showDefaultStatus = showDefaultStatus
            ?? throw new ArgumentNullException(nameof(showDefaultStatus));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        _dismiss = dismiss ?? throw new ArgumentNullException(nameof(dismiss));

        _nativePresentation.TextPointerPressed += OnNativeTextPointerPressed;
        _nativePresentation.TextPointerMoved += OnNativeTextPointerMoved;
        _nativePresentation.TextPointerReleased += OnNativeTextPointerReleased;
        _nativePresentation.TextPointerCanceled += OnNativeTextPointerCanceled;
        _nativePresentation.TextCommandRequested += OnNativeTextCommandRequested;
        _fallbackOverlay.SelectionChanged += OnFallbackTextSelectionChanged;
    }

    public void Start()
    {
        if (_disposed || _recognitionTask is not null || !_recognizer.IsAvailable)
            return;

        _isRecognizing = true;
        _diagnostics.Write(
            AppDiagnosticLevel.Trace,
            "pin.ocr",
            "recognition-started");
        _recognitionTask = RunRecognitionAsync(_lifetime.Token);
        // The toolbar is already rendered with Copy Text disabled. Rebuilding its freshly
        // materialized Button tree here races WinUI's asynchronous template application for the
        // new top-level toolbar window and can terminate the process with XAML 0x802B000A.
        // Automatic OCR is intentionally unobtrusive, so update the toolbar only when recognition
        // reaches a terminal state.
    }

    public bool IsRecognizing => _isRecognizing;

    public bool CanCopyText => _result?.Words.Count is > 0;

    public bool CopyAvailableText()
    {
        if (_result?.Words.Count is not > 0)
            return false;

        string selectedText = _nativePresentation.IsActive
            ? _nativeSelection?.SelectedText ?? ""
            : _fallbackOverlay.SelectedText;
        if (!string.IsNullOrWhiteSpace(selectedText))
            return TryCopyText(selectedText);

        var allText = new OcrTextSelection(_result.Words);
        allText.SelectAll();
        return TryCopyText(allText.SelectedText);
    }

    public bool HandleKey(VirtualKey key, bool controlDown)
    {
        if (controlDown && key == VirtualKey.C)
            return CopySelectedText();
        if (controlDown && key == VirtualKey.A)
            return SelectAllText();
        if (key != VirtualKey.Escape)
            return false;

        if (!ClearTextSelection())
            _dismiss();
        return true;
    }

    public void ResizeFallback(double width, double height)
    {
        if (!_disposed && !_nativePresentation.IsActive && _result is not null)
            _fallbackOverlay.ResizeDisplay(width, height);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isRecognizing = false;
        _lifetime.Cancel();
        _nativePresentation.TextPointerPressed -= OnNativeTextPointerPressed;
        _nativePresentation.TextPointerMoved -= OnNativeTextPointerMoved;
        _nativePresentation.TextPointerReleased -= OnNativeTextPointerReleased;
        _nativePresentation.TextPointerCanceled -= OnNativeTextPointerCanceled;
        _nativePresentation.TextCommandRequested -= OnNativeTextCommandRequested;
        _fallbackOverlay.SelectionChanged -= OnFallbackTextSelectionChanged;
        _fallbackOverlay.Clear();
        _lifetime.Dispose();
    }

    private void OnNativeTextPointerPressed(double x, double y)
    {
        if (_nativeSelection is null || _result is null)
            return;

        (double pixelX, double pixelY) = NativePointToOcr(x, y);
        _nativeDragStartX = pixelX;
        _nativeDragStartY = pixelY;
        _isNativeTextDragging = true;
        _nativeSelection.SelectReadingRange(pixelX, pixelY, pixelX, pixelY);
        RefreshNativeSelection();
    }

    private void OnNativeTextPointerMoved(double x, double y)
    {
        if (!_isNativeTextDragging || _nativeSelection is null)
            return;

        (double pixelX, double pixelY) = NativePointToOcr(x, y);
        _nativeSelection.SelectReadingRange(
            _nativeDragStartX,
            _nativeDragStartY,
            pixelX,
            pixelY);
        RefreshNativeSelection();
    }

    private void OnNativeTextPointerReleased(double x, double y)
    {
        if (!_isNativeTextDragging)
            return;

        OnNativeTextPointerMoved(x, y);
        _isNativeTextDragging = false;
        _showSelectionStatus(_nativeSelection?.SelectedIndices.Count ?? 0);
    }

    private void OnNativeTextPointerCanceled() => _isNativeTextDragging = false;

    private void OnNativeTextCommandRequested(PinTextCommand command)
    {
        switch (command)
        {
            case PinTextCommand.Copy:
                CopySelectedText();
                break;
            case PinTextCommand.SelectAll:
                SelectAllText();
                break;
            case PinTextCommand.Escape:
                if (!ClearTextSelection())
                    _dismiss();
                break;
        }
    }

    private bool IsNativeTextHit(double x, double y)
    {
        if (_nativeSelection is null || _result is null)
            return false;

        var imageFrame = PinHighlight.ImageFrame(_nativePresentation.CurrentFrame);
        double localImageX = x - PinHighlight.Thickness;
        double localImageY = y - PinHighlight.Thickness;
        if (localImageX < 0 || localImageY < 0
            || localImageX > imageFrame.Width || localImageY > imageFrame.Height)
        {
            return false;
        }

        (double pixelX, double pixelY) = NativePointToOcr(x, y);
        double tolerance = Math.Max(
            2,
            3 * _result.PixelWidth / Math.Max(1, imageFrame.Width));
        return _nativeSelection.HitTest(pixelX, pixelY, tolerance) >= 0;
    }

    private (double X, double Y) NativePointToOcr(double x, double y)
    {
        if (_result is null)
            return default;

        var imageFrame = PinHighlight.ImageFrame(_nativePresentation.CurrentFrame);
        return (
            (x - PinHighlight.Thickness) * _result.PixelWidth / Math.Max(1, imageFrame.Width),
            (y - PinHighlight.Thickness) * _result.PixelHeight / Math.Max(1, imageFrame.Height));
    }

    private void RefreshNativeSelection()
    {
        if (_nativeSelection is null || _result is null)
            return;

        var indices = _nativeSelection.SelectedIndices;
        if (_renderedNativeSelection.SetEquals(indices))
            return;

        _renderedNativeSelection.Clear();
        _renderedNativeSelection.UnionWith(indices);
        var selectedBounds = indices
            .Order()
            .Select(index => _result.Words[index].Bounds)
            .ToArray();
        _nativePresentation.UpdateTextSelection(
            selectedBounds,
            _result.PixelWidth,
            _result.PixelHeight);
    }

    private bool CopySelectedText()
    {
        string text = _nativePresentation.IsActive
            ? _nativeSelection?.SelectedText ?? ""
            : _fallbackOverlay.SelectedText;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return TryCopyText(text);
    }

    private bool TryCopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            _clipboard.WriteText(text);
            return true;
        }
        catch (Exception error)
        {
            _diagnostics.Write(
                AppDiagnosticLevel.Warning,
                "pin.ocr",
                "copy-failed",
                exception: error);
            return false;
        }
    }

    private bool SelectAllText()
    {
        if (_nativePresentation.IsActive)
        {
            if (_nativeSelection is null)
                return false;
            _nativeSelection.SelectAll();
            RefreshNativeSelection();
            _showSelectionStatus(_nativeSelection.SelectedIndices.Count);
            return true;
        }

        if (_result?.Words.Count is not > 0)
            return false;
        _fallbackOverlay.SelectAll();
        return true;
    }

    private bool ClearTextSelection()
    {
        if (_nativePresentation.IsActive)
        {
            if (_nativeSelection?.SelectedIndices.Count is not > 0)
                return false;
            _nativeSelection.Clear();
            RefreshNativeSelection();
            _showDefaultStatus();
            return true;
        }

        if (!_fallbackOverlay.HasSelection)
            return false;
        _fallbackOverlay.ClearSelection();
        _showDefaultStatus();
        return true;
    }

    private void OnFallbackTextSelectionChanged() =>
        _showSelectionStatus(_fallbackOverlay.SelectedWordCount);

    private async Task RunRecognitionAsync(CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var result = await _recognizer.RecognizeAsync(
                    _model.RenderedPng,
                    new OcrPixelRect(0, 0, _model.Pixels.Width, _model.Pixels.Height),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyResultOnUiThreadAsync(result, cancellationToken).ConfigureAwait(false);
            _diagnostics.Write(
                AppDiagnosticLevel.Trace,
                "pin.ocr",
                "recognition-completed",
                new Dictionary<string, string?>
                {
                    ["wordCount"] = result.Words.Count.ToString(),
                    ["dimensions"] = $"{result.PixelWidth}x{result.PixelHeight}",
                    ["elapsedMilliseconds"] = System.Diagnostics.Stopwatch
                        .GetElapsedTime(started)
                        .TotalMilliseconds
                        .ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _diagnostics.Write(
                AppDiagnosticLevel.Warning,
                "pin.ocr",
                "recognition-failed",
                exception: error);
        }
        finally
        {
            CompleteRecognitionState();
        }
    }

    private void CompleteRecognitionState()
    {
        void Complete()
        {
            _isRecognizing = false;
            if (!_disposed)
                NotifyStateChanged();
        }

        if (_dispatcher.HasThreadAccess)
        {
            Complete();
            return;
        }

        if (!_dispatcher.TryEnqueue(Complete))
            _isRecognizing = false;
    }

    private void NotifyStateChanged()
    {
        try
        {
            _stateChanged();
        }
        catch (Exception error)
        {
            _diagnostics.Write(
                AppDiagnosticLevel.Warning,
                "pin.ocr",
                "state-notification-failed",
                exception: error);
        }
    }

    private Task ApplyResultOnUiThreadAsync(
        OcrTextResult result,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_disposed)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    _result = result;
                    if (_nativePresentation.IsActive)
                    {
                        _nativeSelection = new OcrTextSelection(result.Words);
                        if (result.Words.Count > 0)
                            _nativePresentation.EnableTextInteraction(IsNativeTextHit);
                    }
                    else if (result.Words.Count > 0)
                    {
                        (double width, double height) = _fallbackSize();
                        _fallbackOverlay.Bind(result, width, height);
                        _enableFallbackClientInteraction();
                    }
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            completion.TrySetException(
                new InvalidOperationException("The pin dispatcher is shutting down."));
        }
        return completion.Task;
    }
}
