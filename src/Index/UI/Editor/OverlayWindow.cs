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
    private const double MinSelectionSize = 5;

    // Selection geometry and rollback live in the platform-independent controller.
    private SelectionController? _selectionController;
    private readonly OverlayWindowTargetController _windowTargetController = new();
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
    private readonly OverlayOcrController _ocrController;
    private readonly OverlayVisualUpdater _visualUpdater;
    private readonly OverlayKeyboardHandler _keyboardHandler;
    private readonly OverlayCaptureCommand _captureCommand;
    private readonly OverlaySelectionInteraction _selectionInteraction;
    private bool _toolbarRefreshQueued;
    private byte[] _frozenPng = [];

    // 选区（覆盖层局部坐标）
    private Rect _selection;

    // 上次截图的选区（物理像素），按 R 键复用
    private CaptureSelection? _previousSelection;

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
        _annotationCanvas.StateChanged += OnAnnotationStateChanged;
        Content = _rootGrid;

        _ocrController = new OverlayOcrController(
            _ocrTextRecognizer,
            _clipboardWriter,
            _diagnostics,
            _liveTextOverlay,
            _annotationCanvas,
            _sizeLabel,
            _sizeLabelText,
            _annotation,
            QueueToolbarRefresh,
            () => _rootGrid.DispatcherQueue,
            () => _hasClosed,
            () => _selection,
            () => _frozenPng,
            () => Coordinates,
            () => _displayDpiScale);

        _captureCommand = new OverlayCaptureCommand(
            _annotation,
            _windowTargetController,
            () => _snapshotIdentity,
            () => Coordinates,
            () => _selection,
            () => _selectionController,
            () => _previousSelection,
            () => CaptureRequested,
            () => CloseOnCapture,
            Dismiss,
            SyncSelectionFromController,
            EnterConfirmedState,
            RequestCancel);

        var capabilities = _ocrTextRecognizer.IsAvailable
            ? new ToolbarHostCapabilities(
                new Dictionary<ToolbarHostMode, ToolbarHostModeCapability>
                {
                    [ToolbarHostMode.LiveText] = new(
                        () => _ocrController.IsActive,
                        _ocrController.Activate,
                        _ocrController.Deactivate)
                })
            : ToolbarHostCapabilities.None;
        _toolbarContext = new ToolbarContext(
            _annotation,
            ToolbarScope.Capture,
            _captureCommand.PerformToolbarCommand,
            capabilities,
            isActionEnabled: _captureCommand.IsToolbarCommandEnabled);

        _visualUpdater = new OverlayVisualUpdater(
            _selectionBorder,
            _dimTop,
            _dimBottom,
            _dimLeft,
            _dimRight,
            _handles,
            _sizeLabel,
            _sizeLabelText,
            _toolbar,
            _annotationCanvas,
            _toolbarRegistry,
            _toolbarContext,
            _ocrController,
            _annotation,
            () => _selection,
            () => _isConfirmed,
            () => FrozenLogicalWidth,
            () => FrozenLogicalHeight,
            () => Coordinates,
            () => _windowTargetController.CandidateCount);

        _selectionInteraction = new OverlaySelectionInteraction(
            _rootGrid,
            _initialFrame,
            _selectionBorder,
            _handlesCanvas,
            _sizeLabel,
            _toolbar,
            _annotationCanvas,
            _ocrController,
            _visualUpdater,
            _windowTargetController,
            () => _isConfirmed,
            value => _isConfirmed = value,
            () => _lastPointerPosition,
            value => _lastPointerPosition = value,
            () => _selectionController,
            () => Coordinates,
            () => _selection,
            ClampPointToCanvas,
            SyncSelectionFromController,
            () => InteractionActivated?.Invoke(this));

        _keyboardHandler = new OverlayKeyboardHandler(
            _ocrController,
            () => _isConfirmed,
            () => _lastPointerPosition,
            () => Coordinates,
            direction => _selectionInteraction.CycleWindowTarget(direction),
            (point, coords) => _windowTargetController.HasTargetAt(point, coords),
            RequestCancel,
            _captureCommand.ConfirmSelection,
            _captureCommand.ApplyPreviousSelection);

        // 事件
        _windowTargetController.PixelEdgeDetectionRequested += () => PixelEdgeDetectionRequested?.Invoke(this);
        _rootGrid.PointerPressed += _selectionInteraction.OnPointerPressed;
        _rootGrid.PointerMoved += _selectionInteraction.OnPointerMoved;
        _rootGrid.PointerReleased += _selectionInteraction.OnPointerReleased;
        _rootGrid.PointerWheelChanged += _selectionInteraction.OnPointerWheelChanged;
        _rootGrid.KeyDown += _keyboardHandler.OnKeyDown;
        Closed += OnClosed;
    }

    /// <summary>设置上次截图的选区，按 R 键可复用。</summary>
    public void SetPreviousSelection(CaptureSelection? selection)
    {
        _previousSelection = selection;
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
        _windowTargetController.Initialize(
            windowTargets ?? Array.Empty<WindowSelectionTarget>(),
            pixelEdgeDetector,
            _snapshotIdentity.Value);
        _lastPointerPosition = default;
        _selection = default;
        _selectionInteraction.HideEditUI();
        _initialFrame.Visibility = Visibility.Visible;
        _visualUpdater.UpdateDim();

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
        => _selectionInteraction.SetPixelEdgeDetector(pixelEdgeDetector);

    private void EnterConfirmedState()
        => _selectionInteraction.EnterConfirmedState();

    private void RequestPixelEdgeDetection()
        => _windowTargetController.RequestPixelEdgeDetection();

    /// <summary>另一块屏开始交互时，清除此屏尚未提交的选区与标注。</summary>
    public void SetSessionInactive()
        => _selectionInteraction.SetSessionInactive(_annotation);

    private void OnAnnotationStateChanged(object? sender, EventArgs args)
    {
        _ocrController.OnAnnotationStateChanged();
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
                _visualUpdater.UpdateToolbarPosition();
        }))
        {
            _toolbarRefreshQueued = false;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _hasClosed = true;
        _ocrController.OnClosed();
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

    private Point ClampPointToCanvas(Point point)
        => new(
            Math.Clamp(point.X, 0, Math.Max(0, FrozenLogicalWidth)),
            Math.Clamp(point.Y, 0, Math.Max(0, FrozenLogicalHeight)));

    private void SyncSelectionFromController()
    {
        _selection = _selectionController?.Selection is { } selection
            ? new Rect(selection.X, selection.Y, selection.Width, selection.Height)
            : new Rect(0, 0, 0, 0);
    }

    private void RequestCancel()
    {
        if (CancelRequested is { } handler)
            handler(this);
        else
            Dismiss();
    }

}
