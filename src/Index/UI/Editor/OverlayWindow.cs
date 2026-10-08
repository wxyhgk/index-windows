using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using WinRT;
using Index.Platform;
using Index.Platform.Windowing;
using Index.Annotation;
using Index.Capture;
using Index.Toolbar;
using Index.UI.Toolbar;
using Index.Platform.Diagnostics;
using Index.Platform.Clipboard;
using Index.Ocr;

namespace Index.UI.Editor;

/// <summary>
/// 全屏覆盖层：冻结画面 + 框选 + 编辑态（选区可调整 + 工具栏）。
/// 对应 macOS 端 OverlayWindow + OverlayView + SelectionModel。
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly IAppDiagnostics _diagnostics;
    private readonly IOcrTextRecognizer _ocrTextRecognizer;
    private readonly IClipboardWriter _clipboardWriter;
    private const double HandleHitRadius = 12;
    private const double MinSelectionSize = 5;

    // Selection geometry and rollback live in the platform-independent controller.
    private SelectionController? _selectionController;
    private WindowTargetNavigator? _windowTargetNavigator;
    private WindowSelectionTarget? _pressedWindowTarget;
    private WindowSelectionTarget? _selectionCaptureTarget;
    private Point _lastPointerPosition;
    private bool _isConfirmed;
    private bool _hasClosed;

    // UI 元素
    private readonly Grid _rootGrid;
    private readonly Image _frozenImage;
    private readonly Rectangle _dimTop, _dimBottom, _dimLeft, _dimRight;
    private readonly Rectangle _selectionBorder;
    private readonly Border _initialFrame;
    private readonly Canvas _handlesCanvas;
    private readonly Shape[] _handles;
    private readonly AnnotationState _annotation = new();
    private readonly ToolbarRegistry _toolbarRegistry = new();
    private readonly ToolbarContext _toolbarContext;
    private readonly ToolbarView _toolbar;
    private readonly AnnotationCanvasView _annotationCanvas;
    private readonly OcrTextOverlayView _liveTextOverlay;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeLabelText;
    private bool _toolbarRefreshQueued;
    private bool _pixelEdgeDetectionRequested;
    private byte[] _frozenPng = [];
    private CancellationTokenSource? _ocrCancellation;
    private int _ocrGeneration;
    private bool _isOcrModeActive;
    private bool _isOcrLoading;
    private bool _copyAllWhenOcrCompletes;

    // 选区（覆盖层局部坐标）
    private Rect _selection;

    // 冻结画面的物理像素尺寸（用于把框选坐标换算成裁剪坐标）
    private int _frozenPixelWidth;
    private int _frozenPixelHeight;
    private double _displayDpiScale = 1;
    private CaptureDisplayIdentity? _snapshotIdentity;

    private double FrozenLogicalWidth => _frozenImage.ActualWidth > 0
        ? _frozenImage.ActualWidth
        : _frozenPixelWidth / Math.Max(_displayDpiScale, 0.01);

    private double FrozenLogicalHeight => _frozenImage.ActualHeight > 0
        ? _frozenImage.ActualHeight
        : _frozenPixelHeight / Math.Max(_displayDpiScale, 0.01);

    private CaptureCoordinateMapper Coordinates => new(
        _frozenPixelWidth,
        _frozenPixelHeight,
        FrozenLogicalWidth,
        FrozenLogicalHeight,
        _displayDpiScale);

    public event Action<CaptureDecision>? CaptureRequested;
    public event Action<OverlayWindow>? InteractionActivated;
    public event Action<OverlayWindow>? PixelEdgeDetectionRequested;
    public event Action<OverlayWindow>? CancelRequested;
    public bool CloseOnCapture { get; set; } = true;

    public OverlayWindow(
        IOcrTextRecognizer ocrTextRecognizer,
        IClipboardWriter clipboardWriter,
        IAppDiagnostics diagnostics)
    {
        _ocrTextRecognizer = ocrTextRecognizer
            ?? throw new ArgumentNullException(nameof(ocrTextRecognizer));
        _clipboardWriter = clipboardWriter
            ?? throw new ArgumentNullException(nameof(clipboardWriter));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        AppWindow.Title = "";
        BuiltinToolbarControls.RegisterCaptureDefaults(_toolbarRegistry);
        AnnotationToolbarControls.RegisterCaptureAnnotationDefaults(_toolbarRegistry);
        AnnotationStyleToolbarControls.RegisterCaptureStyleDefaults(_toolbarRegistry);
        var capabilities = _ocrTextRecognizer.IsAvailable
            ? new ToolbarHostCapabilities(
                new Dictionary<ToolbarHostMode, ToolbarHostModeCapability>
                {
                    [ToolbarHostMode.LiveText] = new(
                        () => _isOcrModeActive,
                        ActivateLiveText,
                        DeactivateLiveText)
                })
            : ToolbarHostCapabilities.None;
        _toolbarContext = new ToolbarContext(
            _annotation,
            ToolbarScope.Capture,
            PerformToolbarCommand,
            capabilities,
            isActionEnabled: IsToolbarCommandEnabled);

        var visuals = new OverlayVisualTree(_annotation);
        _rootGrid = visuals.Root;
        _frozenImage = visuals.FrozenImage;
        _dimTop = visuals.DimTop;
        _dimBottom = visuals.DimBottom;
        _dimLeft = visuals.DimLeft;
        _dimRight = visuals.DimRight;
        _selectionBorder = visuals.SelectionBorder;
        _initialFrame = visuals.InitialFrame;
        _handlesCanvas = visuals.HandlesCanvas;
        _handles = visuals.Handles;
        _sizeLabel = visuals.SizeLabel;
        _sizeLabelText = visuals.SizeLabelText;
        _toolbar = visuals.Toolbar;
        _annotationCanvas = visuals.AnnotationCanvas;
        _liveTextOverlay = visuals.LiveTextOverlay;
        _liveTextOverlay.SelectionChanged += OnLiveTextSelectionChanged;
        _annotationCanvas.StateChanged += OnAnnotationStateChanged;
        Content = _rootGrid;

        // 事件
        _rootGrid.PointerPressed += OnPointerPressed;
        _rootGrid.PointerMoved += OnPointerMoved;
        _rootGrid.PointerReleased += OnPointerReleased;
        _rootGrid.PointerWheelChanged += OnPointerWheelChanged;
        _rootGrid.KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    public void Show(
        DisplaySnapshot snapshot,
        IReadOnlyList<WindowSelectionTarget>? windowTargets = null,
        FrozenPixelEdgeDetector? pixelEdgeDetector = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // 冻结画面：InMemoryRandomAccessStream + BitmapImage（内存流解码快）
        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var writer = new Windows.Storage.Streams.DataWriter(stream);
        writer.WriteBytes(snapshot.PngData);
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        var bitmap = new BitmapImage();
        bitmap.SetSource(stream);
        _frozenImage.Source = bitmap;

        _frozenPixelWidth = snapshot.Width;
        _frozenPixelHeight = snapshot.Height;
        _frozenPng = snapshot.PngData;
        _displayDpiScale = snapshot.DpiScale;
        _snapshotIdentity = new CaptureDisplayIdentity(
            snapshot.DisplayId,
            snapshot.Left,
            snapshot.Top,
            snapshot.Width,
            snapshot.Height,
            snapshot.DpiScale);
        _selectionController = new SelectionController(
            new SelectionRect(0, 0, FrozenLogicalWidth, FrozenLogicalHeight),
            MinSelectionSize,
            MinSelectionSize);
        _windowTargetNavigator = new WindowTargetNavigator(
            windowTargets ?? Array.Empty<WindowSelectionTarget>(),
            pixelEdgeDetector,
            _snapshotIdentity.Value);
        _pressedWindowTarget = null;
        _selectionCaptureTarget = null;
        _pixelEdgeDetectionRequested = false;
        _lastPointerPosition = default;
        _selection = default;
        HideEditUI();
        _initialFrame.Visibility = Visibility.Visible;
        UpdateDim();

        Activate();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var hostBounds = new SourceWindowBounds(
            snapshot.Left,
            snapshot.Top,
            checked(snapshot.Left + snapshot.Width),
            checked(snapshot.Top + snapshot.Height));
        var hostResult = OverlayWindowHost.Apply(
            hwnd,
            new OverlayPhysicalBounds(
                hostBounds.Left,
                hostBounds.Top,
                hostBounds.Width,
                hostBounds.Height));
        var actual = hostResult.ActualBounds;
        _diagnostics.Write(
            AppDiagnosticLevel.Trace,
            "capture.overlay",
            "overlay-shown",
            new Dictionary<string, string?>
            {
                ["displayBounds"] = $"{snapshot.Left},{snapshot.Top},{snapshot.Width}x{snapshot.Height}",
                ["dpiScale"] = snapshot.DpiScale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ["hostSucceeded"] = hostResult.Succeeded.ToString(),
                ["actualBounds"] = actual.ToString(),
            });

        _rootGrid.Focus(FocusState.Programmatic);

    }

    // MARK: - 指针事件

    public void SetPixelEdgeDetector(FrozenPixelEdgeDetector? pixelEdgeDetector)
    {
        _windowTargetNavigator?.SetPixelEdgeDetector(pixelEdgeDetector);
        if (pixelEdgeDetector is not null
            && !_isConfirmed
            && _selectionController is { IsInteracting: false } controller)
        {
            PreviewWindowAt(_lastPointerPosition, controller);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_rootGrid).Properties.IsLeftButtonPressed) return;
        if (_isOcrModeActive)
            DeactivateLiveText();
        RequestPixelEdgeDetection();
        InteractionActivated?.Invoke(this);
        var pos = ClampPointToCanvas(e.GetCurrentPoint(_rootGrid).Position);
        _lastPointerPosition = pos;
        var controller = _selectionController;
        if (controller is null) return;
        _initialFrame.Visibility = Visibility.Collapsed;

        if (_isConfirmed)
        {
            var handle = HitTestHandle(pos);
            if (handle.HasValue)
            {
                controller.BeginResize(ToSelectionHandle(handle.Value), ToSelectionPoint(pos));
                _rootGrid.CapturePointer(e.Pointer);
                return;
            }
            if (_selection.Contains(pos))
            {
                controller.BeginMove(ToSelectionPoint(pos));
                _rootGrid.CapturePointer(e.Pointer);
                return;
            }

            _isConfirmed = false;
            controller.SetSelection(null);
            _selectionCaptureTarget = null;
            SyncSelectionFromController();
            HideEditUI();
        }

        _selectionCaptureTarget = null;
        _pressedWindowTarget = _windowTargetNavigator is { IsCurrentTargetExplicit: true }
            && _windowTargetNavigator.CurrentTarget is { } hovered
            && WindowTargetNavigator.Contains(hovered, ToSelectionPoint(pos))
            ? hovered
            : _windowTargetNavigator?.FindPrimary(ToSelectionPoint(pos), Coordinates);
        if (_pressedWindowTarget is { } target)
        {
            controller.SetSelection(target.Bounds);
            SyncSelectionFromController();
        }

        ApplySelectionBorderStyle(isWindowPreview: false);
        controller.BeginCreate(ToSelectionPoint(pos));
        SyncSelectionFromController();
        _selectionBorder.Visibility = Visibility.Visible;
        _rootGrid.CapturePointer(e.Pointer);
        UpdateDim();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        RequestPixelEdgeDetection();

        var pos = ClampPointToCanvas(e.GetCurrentPoint(_rootGrid).Position);
        _lastPointerPosition = pos;
        if (_selectionController is not { } controller) return;
        if (controller.IsInteracting)
        {
            controller.Update(ToSelectionPoint(pos));
            SyncSelectionFromController();
            if (controller.Interaction == SelectionInteraction.Create && !_selection.IsEmpty)
                _sizeLabel.Visibility = Visibility.Visible;
            UpdateSelectionVisual();
            return;
        }

        if (!_isConfirmed)
            PreviewWindowAt(pos, controller);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _rootGrid.ReleasePointerCapture(e.Pointer);
        if (_selectionController is not { IsInteracting: true } controller) return;
        var interaction = controller.Interaction;
        bool committed = controller.End();
        SyncSelectionFromController();

        if (interaction == SelectionInteraction.Create)
        {
            if (committed)
            {
                // A drag creates a free-form region. The window under the initial press is not an
                // explicit target; resolve the completed selection instead.
                _selectionCaptureTarget = null;
                _pressedWindowTarget = null;
                EnterConfirmedState();
            }
            else if (_pressedWindowTarget is { } target)
            {
                controller.SetSelection(target.Bounds);
                SyncSelectionFromController();
                _selectionCaptureTarget = target;
                _pressedWindowTarget = null;
                EnterConfirmedState();
            }
            else
            {
                _pressedWindowTarget = null;
                _selectionCaptureTarget = null;
                _isConfirmed = false;
                _selectionBorder.Visibility = Visibility.Collapsed;
                _sizeLabel.Visibility = Visibility.Collapsed;
                _initialFrame.Visibility = Visibility.Visible;
                UpdateDim();
            }
            return;
        }

        _isConfirmed = true;
        UpdateSelectionVisual();
    }

    // MARK: - 状态转换

    private void EnterConfirmedState()
    {
        _isConfirmed = true;
        _windowTargetNavigator?.Reset();
        ApplySelectionBorderStyle(isWindowPreview: false);
        _handlesCanvas.Visibility = Visibility.Visible;
        _sizeLabel.Visibility = Visibility.Visible;
        _toolbar.Visibility = Visibility.Visible;
        _annotationCanvas.Visibility = Visibility.Visible;
        UpdateSelectionVisual();
        _rootGrid.Focus(FocusState.Pointer);
    }

    private void HideEditUI()
    {
        DeactivateLiveText();
        _handlesCanvas.Visibility = Visibility.Collapsed;
        _sizeLabel.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = Visibility.Collapsed;
        _annotationCanvas.Visibility = Visibility.Collapsed;
    }

    private void PreviewWindowAt(Point point, SelectionController controller)
    {
        var target = _windowTargetNavigator?.PreviewAt(ToSelectionPoint(point), Coordinates);
        ApplyWindowPreview(target, controller);
    }

    private void ApplyWindowPreview(
        WindowSelectionTarget? target,
        SelectionController controller)
    {
        controller.SetSelection(target?.Bounds);
        SyncSelectionFromController();

        bool found = target is not null;
        _initialFrame.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        _selectionBorder.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        if (found)
        {
            HideEditUI();
            ApplySelectionBorderStyle(isWindowPreview: true);
            _sizeLabel.Visibility = Visibility.Visible;
            UpdateSelectionVisual();
        }
        else
        {
            _sizeLabel.Visibility = Visibility.Collapsed;
            UpdateDim();
        }
    }

    private bool CycleWindowTarget(int direction)
    {
        if (_isConfirmed || _selectionController is not { IsInteracting: false } controller)
            return false;
        if (_windowTargetNavigator is null
            || !_windowTargetNavigator.TryCycle(
                ToSelectionPoint(_lastPointerPosition),
                direction,
                Coordinates,
                out var target))
            return false;

        ApplyWindowPreview(target, controller);
        return true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        RequestPixelEdgeDetection();
        if (_isConfirmed || _selectionController?.IsInteracting != false) return;
        var point = e.GetCurrentPoint(_rootGrid);
        _lastPointerPosition = ClampPointToCanvas(point.Position);
        int direction = point.Properties.MouseWheelDelta < 0 ? 1 : -1;
        if (CycleWindowTarget(direction))
            e.Handled = true;
    }

    private void RequestPixelEdgeDetection()
    {
        if (_pixelEdgeDetectionRequested)
            return;

        _pixelEdgeDetectionRequested = true;
        PixelEdgeDetectionRequested?.Invoke(this);
    }

    private void ActivateLiveText()
    {
        if (_isOcrModeActive || _selection.IsEmpty || _frozenPng.Length == 0)
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

        var crop = Coordinates.ToCropRect(new SelectionRect(
            _selection.X,
            _selection.Y,
            _selection.Width,
            _selection.Height));
        WriteOcrDiagnostic(
            AppDiagnosticLevel.Trace,
            "recognition-started",
            new Dictionary<string, string?>
            {
                ["crop"] = $"{crop.X},{crop.Y},{crop.Width}x{crop.Height}",
                ["dpiScale"] = _displayDpiScale.ToString(
                    "F3",
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        _ = RecognizeLiveTextAsync(
            generation,
            cancellation,
            new OcrPixelRect(crop.X, crop.Y, crop.Width, crop.Height));
        QueueToolbarRefresh();
    }

    private void DeactivateLiveText()
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
        UpdateSizeLabel();
        QueueToolbarRefresh();
    }

    private async Task RecognizeLiveTextAsync(
        int generation,
        CancellationTokenSource cancellation,
        OcrPixelRect crop)
    {
        try
        {
            var result = await _ocrTextRecognizer
                .RecognizeAsync(_frozenPng, crop, cancellation.Token)
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

                PositionLiveTextOverlay();
                _liveTextOverlay.Bind(result, _selection.Width, _selection.Height);
                if (_copyAllWhenOcrCompletes)
                {
                    _copyAllWhenOcrCompletes = false;
                    _liveTextOverlay.SelectAll();
                    CopySelectedLiveText();
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
            if (_hasClosed
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
            if (_rootGrid.DispatcherQueue.HasThreadAccess)
                Apply();
            else if (!_rootGrid.DispatcherQueue.TryEnqueue(Apply))
                ReleaseOcrCancellation(cancellation);
        }
        catch
        {
            ReleaseOcrCancellation(cancellation);
        }
    }

    private void OnLiveTextSelectionChanged()
    {
        if (!_isOcrModeActive || _isOcrLoading)
            return;

        _sizeLabelText.Text = _liveTextOverlay.HasSelection
            ? $"已选择 {_liveTextOverlay.SelectedWordCount} 个词 · Ctrl+C 复制"
            : "拖动选择文字 · Ctrl+C 复制";
    }

    private void CopySelectedLiveText()
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

    private void CopyAllLiveText()
    {
        if (!_isOcrModeActive)
        {
            _copyAllWhenOcrCompletes = true;
            ActivateLiveText();
            return;
        }

        if (_isOcrLoading)
        {
            _copyAllWhenOcrCompletes = true;
            _sizeLabelText.Text = "正在识别，完成后复制全部文字…";
            return;
        }

        _liveTextOverlay.SelectAll();
        CopySelectedLiveText();
    }

    /// <summary>另一块屏开始交互时，清除此屏尚未提交的选区与标注。</summary>
    public void SetSessionInactive()
    {
        DeactivateLiveText();
        _selectionController?.Cancel();
        _selectionController?.SetSelection(null);
        _annotation.Clear();
        SyncSelectionFromController();
        _isConfirmed = false;
        _windowTargetNavigator?.Reset();
        _selectionCaptureTarget = null;
        HideEditUI();
        _initialFrame.Visibility = Visibility.Collapsed;
        _selectionBorder.Visibility = Visibility.Collapsed;
        UpdateDim();
    }

    private void ConfirmSelection(string actionId)
    {
        if (_snapshotIdentity is not { } snapshotIdentity)
            throw new InvalidOperationException("覆盖窗口尚未绑定显示器快照。");

        // 框选坐标是覆盖层内部坐标，换算成冻结画面的物理像素。
        var coordinates = Coordinates;
        var crop = coordinates.ToCropRect(new SelectionRect(
            _selection.X,
            _selection.Y,
            _selection.Width,
            _selection.Height));

        var decision = new CaptureDecision(
            actionId,
            new CaptureSelection
            {
                Display = snapshotIdentity,
                X = crop.X,
                Y = crop.Y,
                Width = crop.Width,
                Height = crop.Height,
                Layers = _annotation.ExportLayers(
                    new LRect(0, 0, _selection.Width, _selection.Height),
                    coordinates.ScaleX,
                    coordinates.ScaleY)
            },
            FindTargetWindowHandle());

        // Release the full-screen overlay before any potentially expensive action runs.
        var handler = CaptureRequested;
        if (CloseOnCapture)
            Dismiss();
        handler?.Invoke(decision);
    }

    private void OnAnnotationStateChanged(object? sender, EventArgs args)
    {
        if (_annotation.Tool.HasValue && _isOcrModeActive)
            DeactivateLiveText();
        _annotationCanvas.IsHitTestVisible = !_isOcrModeActive
            && (_annotation.Tool.HasValue || !_annotation.IsEmpty);
        QueueToolbarRefresh();
    }

    private void QueueToolbarRefresh()
    {
        if (_hasClosed || _toolbarRefreshQueued) return;

        _toolbarRefreshQueued = true;
        if (!_rootGrid.DispatcherQueue.TryEnqueue(() =>
        {
            _toolbarRefreshQueued = false;
            if (!_hasClosed)
                UpdateToolbarPosition();
        }))
        {
            _toolbarRefreshQueued = false;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _hasClosed = true;
        DeactivateLiveText();
        _liveTextOverlay.SelectionChanged -= OnLiveTextSelectionChanged;
        _annotationCanvas.StateChanged -= OnAnnotationStateChanged;
        _annotationCanvas.Dispose();
        Closed -= OnClosed;
    }

    public void Dismiss()
    {
        if (!_hasClosed)
            Close();
    }

    /// <summary>重新置顶仍存活的原生覆盖窗口；句柄已失效时返回 false。</summary>
    public bool TryReactivate(DisplaySnapshot snapshot)
    {
        if (_hasClosed) return false;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (!OverlayWindowHost.IsWindowUsable(hwnd)) return false;
            Activate();
            var hostBounds = new SourceWindowBounds(
                snapshot.Left,
                snapshot.Top,
                checked(snapshot.Left + snapshot.Width),
                checked(snapshot.Top + snapshot.Height));
            var result = OverlayWindowHost.Apply(
                hwnd,
                new OverlayPhysicalBounds(
                    hostBounds.Left,
                    hostBounds.Top,
                    hostBounds.Width,
                    hostBounds.Height));
            Activate();
            return result.Succeeded && OverlayWindowHost.IsWindowShown(hwnd);
        }
        catch
        {
            return false;
        }
    }

    private void PerformToolbarCommand(string commandID)
    {
        switch (commandID)
        {
            case ToolbarCommandIds.Pin:
            case ToolbarCommandIds.Copy:
            case ToolbarCommandIds.Complete:
            case ToolbarCommandIds.HighResolution4K:
                ConfirmSelection(commandID);
                break;
            case ToolbarCommandIds.Cancel:
                RequestCancel();
                break;
        }
    }

    // MARK: - 视觉更新

    private void UpdateSelectionVisual()
    {
        _selectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selectionBorder, _selection.X);
        Canvas.SetTop(_selectionBorder, _selection.Y);
        _selectionBorder.Width = _selection.Width;
        _selectionBorder.Height = _selection.Height;

        Canvas.SetLeft(_annotationCanvas, _selection.X);
        Canvas.SetTop(_annotationCanvas, _selection.Y);
        _annotationCanvas.Width = _selection.Width;
        _annotationCanvas.Height = _selection.Height;
        _annotationCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, _selection.Width, _selection.Height)
        };
        _annotationCanvas.IsHitTestVisible = !_isOcrModeActive
            && (_annotation.Tool.HasValue || !_annotation.IsEmpty);

        PositionLiveTextOverlay();

        UpdateDim();
        UpdateHandles();
        UpdateSizeLabel();
        UpdateToolbarPosition();
    }

    private bool IsToolbarCommandEnabled(string commandId) =>
        commandId != ToolbarCommandIds.HighResolution4K
        || FindTargetWindowHandle() != nint.Zero;

    private nint FindTargetWindowHandle()
    {
        if (_selection.IsEmpty || _windowTargetNavigator is null)
            return nint.Zero;
        return _windowTargetNavigator.ResolveCaptureHandle(
            _selectionCaptureTarget,
            new SelectionRect(
                _selection.X,
                _selection.Y,
                _selection.Width,
                _selection.Height),
            Coordinates);
    }

    private void ApplySelectionBorderStyle(bool isWindowPreview)
    {
        _selectionBorder.Stroke = new SolidColorBrush(isWindowPreview
            ? Windows.UI.Color.FromArgb(0xFF, 0x36, 0x92, 0xFF)
            : Colors.White);
        _selectionBorder.StrokeThickness = isWindowPreview ? 2.25 : 1.75;
    }

    private void UpdateDim()
    {
        double w = FrozenLogicalWidth;
        double h = FrozenLogicalHeight;

        if (_selection.IsEmpty)
        {
            _dimTop.Width = w; _dimTop.Height = h;
            Canvas.SetLeft(_dimTop, 0); Canvas.SetTop(_dimTop, 0);
            _dimBottom.Width = 0; _dimBottom.Height = 0;
            _dimLeft.Width = 0; _dimLeft.Height = 0;
            _dimRight.Width = 0; _dimRight.Height = 0;
            return;
        }

        var constrained = new LRect(
            _selection.X,
            _selection.Y,
            _selection.Width,
            _selection.Height)
            .IntersectedWithBounds(w, h);
        double sx = constrained.X, sy = constrained.Y;
        double sw = constrained.W, sh = constrained.H;
        // 上：从窗口顶到选区顶
        _dimTop.Width = w; _dimTop.Height = sy;
        Canvas.SetLeft(_dimTop, 0); Canvas.SetTop(_dimTop, 0);
        // 下：从选区底到窗口底
        _dimBottom.Width = w; _dimBottom.Height = Math.Max(0, h - sy - sh);
        Canvas.SetLeft(_dimBottom, 0); Canvas.SetTop(_dimBottom, sy + sh);
        // 左：从窗口左到选区左
        _dimLeft.Width = sx; _dimLeft.Height = sh;
        Canvas.SetLeft(_dimLeft, 0); Canvas.SetTop(_dimLeft, sy);
        // 右：从选区右到窗口右
        _dimRight.Width = Math.Max(0, w - sx - sw); _dimRight.Height = sh;
        Canvas.SetLeft(_dimRight, sx + sw); Canvas.SetTop(_dimRight, sy);
    }

    private static readonly ResizeHandle[] HandleOrder =
    {
        ResizeHandle.TopLeft, ResizeHandle.Top, ResizeHandle.TopRight,
        ResizeHandle.Right, ResizeHandle.BottomRight,
        ResizeHandle.Bottom, ResizeHandle.BottomLeft, ResizeHandle.Left
    };

    private Point ClampPointToCanvas(Point point)
        => new(
            Math.Clamp(point.X, 0, Math.Max(0, FrozenLogicalWidth)),
            Math.Clamp(point.Y, 0, Math.Max(0, FrozenLogicalHeight)));

    private static SelectionPoint ToSelectionPoint(Point point) => new(point.X, point.Y);

    private static SelectionResizeHandle ToSelectionHandle(ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => SelectionResizeHandle.TopLeft,
        ResizeHandle.Top => SelectionResizeHandle.Top,
        ResizeHandle.TopRight => SelectionResizeHandle.TopRight,
        ResizeHandle.Right => SelectionResizeHandle.Right,
        ResizeHandle.BottomRight => SelectionResizeHandle.BottomRight,
        ResizeHandle.Bottom => SelectionResizeHandle.Bottom,
        ResizeHandle.BottomLeft => SelectionResizeHandle.BottomLeft,
        ResizeHandle.Left => SelectionResizeHandle.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, null)
    };

    private void SyncSelectionFromController()
    {
        _selection = _selectionController?.Selection is { } selection
            ? new Rect(selection.X, selection.Y, selection.Width, selection.Height)
            : new Rect(0, 0, 0, 0);
    }

    private void UpdateHandles()
    {
        if (!_isConfirmed)
        {
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
            return;
        }

        var lRect = new LRect(_selection.X, _selection.Y, _selection.Width, _selection.Height);
        for (int i = 0; i < 8; i++)
        {
            var pos = HandleOrder[i].PointIn(lRect);
            _handles[i].Visibility = Visibility.Visible;
            Canvas.SetLeft(_handles[i], pos.X - OverlayVisualTree.HandleSize / 2);
            Canvas.SetTop(_handles[i], pos.Y - OverlayVisualTree.HandleSize / 2);
        }
    }

    private void UpdateSizeLabel()
    {
        if (_selection.IsEmpty) return;
        var pixelSize = Coordinates.ToDisplaySize(new SelectionRect(
            _selection.X,
            _selection.Y,
            _selection.Width,
            _selection.Height));
        string cycleHint = !_isConfirmed && _windowTargetNavigator?.CandidateCount > 1
            ? "  ·  Tab 切换"
            : "";
        _sizeLabelText.Text = $"{pixelSize.Width} × {pixelSize.Height}{cycleHint}";
        // 尺寸贴选区左上角，避免与下方工具栏争位置。
        double labelY = _selection.Y >= 36 ? _selection.Y - 28 : _selection.Y + 8;
        double labelX = Math.Clamp(_selection.X, 4, Math.Max(4, FrozenLogicalWidth - 170));
        Canvas.SetLeft(_sizeLabel, labelX);
        Canvas.SetTop(_sizeLabel, labelY);
    }

    private void PositionLiveTextOverlay()
    {
        Canvas.SetLeft(_liveTextOverlay, _selection.X);
        Canvas.SetTop(_liveTextOverlay, _selection.Y);
        _liveTextOverlay.Width = _selection.Width;
        _liveTextOverlay.Height = _selection.Height;
        _liveTextOverlay.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, _selection.Width, _selection.Height)
        };
    }

    private void UpdateToolbarPosition()
    {
        if (_selection.IsEmpty || !_isConfirmed) return;
        double canvasW = FrozenLogicalWidth;
        double canvasH = FrozenLogicalHeight;
        var layout = _toolbar.Update(
            _toolbarRegistry,
            _toolbarContext,
            new ToolbarRect(0, 0, canvasW, canvasH),
            new ToolbarRect(_selection.X, _selection.Y, _selection.Width, _selection.Height));

        Canvas.SetLeft(_toolbar, layout.X);
        Canvas.SetTop(_toolbar, layout.Y);
    }

    // MARK: - 命中测试

    private ResizeHandle? HitTestHandle(Point pos)
    {
        var lRect = new LRect(_selection.X, _selection.Y, _selection.Width, _selection.Height);
        foreach (var handle in HandleOrder)
        {
            var hPos = handle.PointIn(lRect);
            double dx = pos.X - hPos.X;
            double dy = pos.Y - hPos.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= HandleHitRadius)
                return handle;
        }
        return null;
    }

    // MARK: - 键盘

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        bool controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.C when controlDown && _isOcrModeActive:
                CopySelectedLiveText();
                e.Handled = true;
                break;

            case VirtualKey.C when shiftDown && _isConfirmed:
                CopyAllLiveText();
                e.Handled = true;
                break;

            case VirtualKey.Tab:
                bool cycled = CycleWindowTarget(shiftDown ? -1 : 1);
                bool hasTarget = !_isConfirmed
                    && _windowTargetNavigator?.HasTargetAt(
                        ToSelectionPoint(_lastPointerPosition),
                        Coordinates) == true;
                if (cycled || hasTarget)
                    e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (_isOcrModeActive && _liveTextOverlay.HasSelection)
                    _liveTextOverlay.ClearSelection();
                else if (_isOcrModeActive)
                    DeactivateLiveText();
                else
                    RequestCancel();
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                if (_isConfirmed)
                {
                    ConfirmSelection(ToolbarCommandIds.Complete);
                    e.Handled = true;
                }
                break;
        }
    }

    private void RequestCancel()
    {
        if (CancelRequested is { } handler)
            handler(this);
        else
            Dismiss();
    }

}
