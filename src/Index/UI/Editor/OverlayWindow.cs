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

namespace Index.UI.Editor;

/// <summary>
/// 全屏覆盖层：冻结画面 + 框选 + 编辑态（选区可调整 + 工具栏）。
/// 对应 macOS 端 OverlayWindow + OverlayView + SelectionModel。
/// </summary>
public sealed class OverlayWindow : Window
{
    private const double HandleHitRadius = 12;
    private const double MinSelectionSize = 5;

    // Selection geometry and rollback live in the platform-independent controller.
    private SelectionController? _selectionController;
    private WindowTargetNavigator? _windowTargetNavigator;
    private WindowSelectionTarget? _pressedWindowTarget;
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
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeLabelText;
    private bool _toolbarRefreshQueued;
    private bool _pixelEdgeDetectionRequested;

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

    public OverlayWindow()
    {
        AppWindow.Title = "";
        BuiltinToolbarControls.RegisterCaptureDefaults(_toolbarRegistry);
        AnnotationToolbarControls.RegisterCaptureAnnotationDefaults(_toolbarRegistry);
        AnnotationStyleToolbarControls.RegisterCaptureStyleDefaults(_toolbarRegistry);
        _toolbarContext = new ToolbarContext(
            _annotation,
            ToolbarScope.Capture,
            PerformToolbarCommand);

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
        _pixelEdgeDetectionRequested = false;
        _lastPointerPosition = default;
        _selection = default;
        HideEditUI();
        _initialFrame.Visibility = Visibility.Visible;
        UpdateDim();

        Activate();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var hostResult = OverlayWindowHost.Apply(
            hwnd,
            new OverlayPhysicalBounds(
                snapshot.Left,
                snapshot.Top,
                snapshot.Width,
                snapshot.Height));
        var actual = hostResult.ActualBounds;
        System.IO.File.AppendAllText(@"C:\temp\index_capture.log",
            $"[overlay] Show: display=({snapshot.Left},{snapshot.Top},{snapshot.Width}x{snapshot.Height}) " +
            $"dpiScale={snapshot.DpiScale:F3} host={hostResult.Succeeded} " +
            $"actual={actual}\n");

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
            SyncSelectionFromController();
            HideEditUI();
        }

        _pressedWindowTarget = _windowTargetNavigator?.CurrentTarget is { } hovered
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
                _pressedWindowTarget = null;
                EnterConfirmedState();
            }
            else if (_pressedWindowTarget is { } target)
            {
                controller.SetSelection(target.Bounds);
                SyncSelectionFromController();
                _pressedWindowTarget = null;
                EnterConfirmedState();
            }
            else
            {
                _pressedWindowTarget = null;
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

    /// <summary>另一块屏开始交互时，清除此屏尚未提交的选区与标注。</summary>
    public void SetSessionInactive()
    {
        _selectionController?.Cancel();
        _selectionController?.SetSelection(null);
        _annotation.Clear();
        SyncSelectionFromController();
        _isConfirmed = false;
        _windowTargetNavigator?.Reset();
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
            });

        // Release the full-screen overlay before any potentially expensive action runs.
        var handler = CaptureRequested;
        if (CloseOnCapture)
            Dismiss();
        handler?.Invoke(decision);
    }

    private void OnAnnotationStateChanged(object? sender, EventArgs args)
    {
        _annotationCanvas.IsHitTestVisible = _annotation.Tool.HasValue || !_annotation.IsEmpty;
        if (_toolbarRefreshQueued) return;

        _toolbarRefreshQueued = true;
        _rootGrid.DispatcherQueue.TryEnqueue(() =>
        {
            _toolbarRefreshQueued = false;
            UpdateToolbarPosition();
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _hasClosed = true;
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
            var result = OverlayWindowHost.Apply(
                hwnd,
                new OverlayPhysicalBounds(
                    snapshot.Left,
                    snapshot.Top,
                    snapshot.Width,
                    snapshot.Height));
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
        _annotationCanvas.IsHitTestVisible = _annotation.Tool.HasValue || !_annotation.IsEmpty;

        UpdateDim();
        UpdateHandles();
        UpdateSizeLabel();
        UpdateToolbarPosition();
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
        switch (e.Key)
        {
            case VirtualKey.Tab:
                bool shiftDown = InputKeyboardSource
                    .GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(CoreVirtualKeyStates.Down);
                bool cycled = CycleWindowTarget(shiftDown ? -1 : 1);
                bool hasTarget = !_isConfirmed
                    && _windowTargetNavigator?.HasTargetAt(
                        ToSelectionPoint(_lastPointerPosition),
                        Coordinates) == true;
                if (cycled || hasTarget)
                    e.Handled = true;
                break;

            case VirtualKey.Escape:
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
