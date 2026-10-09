using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.UI.Toolbar;

namespace Index.UI.Editor;

/// <summary>
/// 覆盖层选区交互：指针事件（按下/移动/释放/滚轮）与状态转换
/// （确认、隐藏编辑 UI、窗口预览、循环目标）。
/// 从 OverlayWindow 拆出，集中管理选区交互状态机。
/// </summary>
internal sealed class OverlaySelectionInteraction
{
    private readonly Grid _rootGrid;
    private readonly Border _initialFrame;
    private readonly Rectangle _selectionBorder;
    private readonly Canvas _handlesCanvas;
    private readonly Border _sizeLabel;
    private readonly ToolbarView _toolbar;
    private readonly AnnotationCanvasView _annotationCanvas;

    private readonly OverlayOcrController _ocrController;
    private readonly OverlayVisualUpdater _visualUpdater;
    private readonly OverlayWindowTargetController _windowTargetController;

    private readonly Func<bool> _getIsConfirmed;
    private readonly Action<bool> _setIsConfirmed;
    private readonly Func<Point> _getLastPointerPosition;
    private readonly Action<Point> _setLastPointerPosition;
    private readonly Func<SelectionController?> _getSelectionController;
    private readonly Func<CaptureCoordinateMapper> _getCoordinates;
    private readonly Func<Rect> _getSelection;
    private readonly Func<Point, Point> _clampPointToCanvas;
    private readonly Action _syncSelectionFromController;
    private readonly Action _raiseInteractionActivated;

    public OverlaySelectionInteraction(
        Grid rootGrid,
        Border initialFrame,
        Rectangle selectionBorder,
        Canvas handlesCanvas,
        Border sizeLabel,
        ToolbarView toolbar,
        AnnotationCanvasView annotationCanvas,
        OverlayOcrController ocrController,
        OverlayVisualUpdater visualUpdater,
        OverlayWindowTargetController windowTargetController,
        Func<bool> getIsConfirmed,
        Action<bool> setIsConfirmed,
        Func<Point> getLastPointerPosition,
        Action<Point> setLastPointerPosition,
        Func<SelectionController?> getSelectionController,
        Func<CaptureCoordinateMapper> getCoordinates,
        Func<Rect> getSelection,
        Func<Point, Point> clampPointToCanvas,
        Action syncSelectionFromController,
        Action raiseInteractionActivated)
    {
        _rootGrid = rootGrid;
        _initialFrame = initialFrame;
        _selectionBorder = selectionBorder;
        _handlesCanvas = handlesCanvas;
        _sizeLabel = sizeLabel;
        _toolbar = toolbar;
        _annotationCanvas = annotationCanvas;
        _ocrController = ocrController;
        _visualUpdater = visualUpdater;
        _windowTargetController = windowTargetController;
        _getIsConfirmed = getIsConfirmed;
        _setIsConfirmed = setIsConfirmed;
        _getLastPointerPosition = getLastPointerPosition;
        _setLastPointerPosition = setLastPointerPosition;
        _getSelectionController = getSelectionController;
        _getCoordinates = getCoordinates;
        _getSelection = getSelection;
        _clampPointToCanvas = clampPointToCanvas;
        _syncSelectionFromController = syncSelectionFromController;
        _raiseInteractionActivated = raiseInteractionActivated;
    }

    public void SetPixelEdgeDetector(FrozenPixelEdgeDetector? pixelEdgeDetector)
    {
        _windowTargetController.SetPixelEdgeDetector(pixelEdgeDetector);
        if (pixelEdgeDetector is not null
            && !_getIsConfirmed()
            && _getSelectionController() is { IsInteracting: false } controller)
        {
            PreviewWindowAt(_getLastPointerPosition(), controller);
        }
    }

    public void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_rootGrid).Properties.IsLeftButtonPressed) return;
        if (_ocrController.IsActive)
            _ocrController.Deactivate();
        RequestPixelEdgeDetection();
        _raiseInteractionActivated();
        var pos = _clampPointToCanvas(e.GetCurrentPoint(_rootGrid).Position);
        _setLastPointerPosition(pos);
        var controller = _getSelectionController();
        if (controller is null) return;
        _initialFrame.Visibility = Visibility.Collapsed;

        if (_getIsConfirmed())
        {
            var handle = _visualUpdater.HitTestHandle(pos);
            if (handle.HasValue)
            {
                controller.BeginResize(SelectionPointMapper.ToSelectionHandle(handle.Value), SelectionPointMapper.ToSelectionPoint(pos));
                _rootGrid.CapturePointer(e.Pointer);
                return;
            }
            if (_getSelection().Contains(pos))
            {
                controller.BeginMove(SelectionPointMapper.ToSelectionPoint(pos));
                _rootGrid.CapturePointer(e.Pointer);
                return;
            }

            _setIsConfirmed(false);
            controller.SetSelection(null);
            _windowTargetController.ClearCaptureTarget();
            _syncSelectionFromController();
            HideEditUI();
        }

        _windowTargetController.ClearCaptureTarget();
        _windowTargetController.FindPressedTarget(SelectionPointMapper.ToSelectionPoint(pos), _getCoordinates());
        if (_windowTargetController.PressedTarget is { } target)
        {
            controller.SetSelection(target.Bounds);
            _syncSelectionFromController();
        }

        _visualUpdater.ApplySelectionBorderStyle(isWindowPreview: false);
        controller.BeginCreate(SelectionPointMapper.ToSelectionPoint(pos));
        _syncSelectionFromController();
        _selectionBorder.Visibility = Visibility.Visible;
        _rootGrid.CapturePointer(e.Pointer);
        _visualUpdater.UpdateDim();
    }

    public void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        RequestPixelEdgeDetection();

        var pos = _clampPointToCanvas(e.GetCurrentPoint(_rootGrid).Position);
        _setLastPointerPosition(pos);
        if (_getSelectionController() is not { } controller) return;
        if (controller.IsInteracting)
        {
            controller.Update(SelectionPointMapper.ToSelectionPoint(pos));
            _syncSelectionFromController();
            if (controller.Interaction == SelectionInteraction.Create && !_getSelection().IsEmpty)
                _sizeLabel.Visibility = Visibility.Visible;
            _visualUpdater.UpdateSelectionVisual();
            return;
        }

        if (!_getIsConfirmed())
            PreviewWindowAt(pos, controller);
    }

    public void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _rootGrid.ReleasePointerCapture(e.Pointer);
        if (_getSelectionController() is not { IsInteracting: true } controller) return;
        var interaction = controller.Interaction;
        bool committed = controller.End();
        _syncSelectionFromController();

        if (interaction == SelectionInteraction.Create)
        {
            if (committed)
            {
                // A drag creates a free-form region. The window under the initial press is not an
                // explicit target; resolve the completed selection instead.
                _windowTargetController.ClearCaptureTarget();
                _windowTargetController.ClearPressedTarget();
                EnterConfirmedState();
            }
            else if (_windowTargetController.PressedTarget is { } target)
            {
                controller.SetSelection(target.Bounds);
                _syncSelectionFromController();
                _windowTargetController.SetCaptureTarget(target);
                _windowTargetController.ClearPressedTarget();
                EnterConfirmedState();
            }
            else
            {
                _windowTargetController.ClearPressedTarget();
                _windowTargetController.ClearCaptureTarget();
                _setIsConfirmed(false);
                _selectionBorder.Visibility = Visibility.Collapsed;
                _sizeLabel.Visibility = Visibility.Collapsed;
                _initialFrame.Visibility = Visibility.Visible;
                _visualUpdater.UpdateDim();
            }
            return;
        }

        _setIsConfirmed(true);
        _visualUpdater.UpdateSelectionVisual();
    }

    public void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        RequestPixelEdgeDetection();
        if (_getIsConfirmed() || _getSelectionController()?.IsInteracting != false) return;
        var point = e.GetCurrentPoint(_rootGrid);
        _setLastPointerPosition(_clampPointToCanvas(point.Position));
        int direction = point.Properties.MouseWheelDelta < 0 ? 1 : -1;
        if (CycleWindowTarget(direction))
            e.Handled = true;
    }

    public void EnterConfirmedState()
    {
        _setIsConfirmed(true);
        _windowTargetController.Reset();
        _visualUpdater.ApplySelectionBorderStyle(isWindowPreview: false);
        _handlesCanvas.Visibility = Visibility.Visible;
        _sizeLabel.Visibility = Visibility.Visible;
        _toolbar.Visibility = Visibility.Visible;
        _annotationCanvas.Visibility = Visibility.Visible;
        _visualUpdater.UpdateSelectionVisual();
        _rootGrid.Focus(FocusState.Pointer);
    }

    public void HideEditUI()
    {
        _ocrController.Deactivate();
        _handlesCanvas.Visibility = Visibility.Collapsed;
        _sizeLabel.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = Visibility.Collapsed;
        _annotationCanvas.Visibility = Visibility.Collapsed;
    }

    private void PreviewWindowAt(Point point, SelectionController controller)
    {
        var target = _windowTargetController.PreviewAt(SelectionPointMapper.ToSelectionPoint(point), _getCoordinates());
        ApplyWindowPreview(target, controller);
    }

    private void ApplyWindowPreview(
        WindowSelectionTarget? target,
        SelectionController controller)
    {
        controller.SetSelection(target?.Bounds);
        _syncSelectionFromController();

        bool found = target is not null;
        _initialFrame.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
        _selectionBorder.Visibility = found ? Visibility.Visible : Visibility.Collapsed;
        if (found)
        {
            HideEditUI();
            _visualUpdater.ApplySelectionBorderStyle(isWindowPreview: true);
            _sizeLabel.Visibility = Visibility.Visible;
            _visualUpdater.UpdateSelectionVisual();
        }
        else
        {
            _sizeLabel.Visibility = Visibility.Collapsed;
            _visualUpdater.UpdateDim();
        }
    }

    public bool CycleWindowTarget(int direction)
    {
        if (_getIsConfirmed() || _getSelectionController() is not { IsInteracting: false } controller)
            return false;
        if (!_windowTargetController.TryCycle(
                SelectionPointMapper.ToSelectionPoint(_getLastPointerPosition()),
                direction,
                _getCoordinates(),
                out var target))
            return false;

        ApplyWindowPreview(target, controller);
        return true;
    }

    private void RequestPixelEdgeDetection()
        => _windowTargetController.RequestPixelEdgeDetection();

    /// <summary>另一块屏开始交互时，清除此屏尚未提交的选区与标注。</summary>
    public void SetSessionInactive(
        AnnotationState annotation)
    {
        _ocrController.Deactivate();
        _getSelectionController()?.Cancel();
        _getSelectionController()?.SetSelection(null);
        annotation.Clear();
        _syncSelectionFromController();
        _setIsConfirmed(false);
        _windowTargetController.Reset();
        HideEditUI();
        _initialFrame.Visibility = Visibility.Collapsed;
        _selectionBorder.Visibility = Visibility.Collapsed;
        _visualUpdater.UpdateDim();
    }
}
