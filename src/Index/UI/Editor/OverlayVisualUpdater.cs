using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Toolbar;
using Index.UI.Toolbar;

namespace Index.UI.Editor;

/// <summary>
/// 覆盖层视觉更新器：管理选区边框、dim 遮罩、resize handles、尺寸标签和工具栏位置。
/// 从 OverlayWindow 拆出，减少主窗口的 UI 元素引用和视觉计算逻辑。
/// </summary>
internal sealed class OverlayVisualUpdater
{
    private readonly Rectangle _selectionBorder;
    private readonly Rectangle _dimTop, _dimBottom, _dimLeft, _dimRight;
    private readonly Shape[] _handles;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeLabelText;
    private readonly ToolbarView _toolbar;
    private readonly AnnotationCanvasView _annotationCanvas;
    private readonly ToolbarRegistry _toolbarRegistry;
    private readonly ToolbarContext _toolbarContext;
    private readonly OverlayOcrController _ocrController;
    private readonly AnnotationState _annotation;

    private readonly Func<Rect> _getSelection;
    private readonly Func<bool> _getIsConfirmed;
    private readonly Func<double> _getFrozenLogicalWidth;
    private readonly Func<double> _getFrozenLogicalHeight;
    private readonly Func<CaptureCoordinateMapper> _getCoordinates;
    private readonly Func<int> _getWindowTargetCandidateCount;

    private static readonly ResizeHandle[] HandleOrder =
    {
        ResizeHandle.TopLeft, ResizeHandle.Top, ResizeHandle.TopRight,
        ResizeHandle.Right, ResizeHandle.BottomRight,
        ResizeHandle.Bottom, ResizeHandle.BottomLeft, ResizeHandle.Left
    };

    public OverlayVisualUpdater(
        Rectangle selectionBorder,
        Rectangle dimTop,
        Rectangle dimBottom,
        Rectangle dimLeft,
        Rectangle dimRight,
        Shape[] handles,
        Border sizeLabel,
        TextBlock sizeLabelText,
        ToolbarView toolbar,
        AnnotationCanvasView annotationCanvas,
        ToolbarRegistry toolbarRegistry,
        ToolbarContext toolbarContext,
        OverlayOcrController ocrController,
        AnnotationState annotation,
        Func<Rect> getSelection,
        Func<bool> getIsConfirmed,
        Func<double> getFrozenLogicalWidth,
        Func<double> getFrozenLogicalHeight,
        Func<CaptureCoordinateMapper> getCoordinates,
        Func<int> getWindowTargetCandidateCount)
    {
        _selectionBorder = selectionBorder;
        _dimTop = dimTop;
        _dimBottom = dimBottom;
        _dimLeft = dimLeft;
        _dimRight = dimRight;
        _handles = handles;
        _sizeLabel = sizeLabel;
        _sizeLabelText = sizeLabelText;
        _toolbar = toolbar;
        _annotationCanvas = annotationCanvas;
        _toolbarRegistry = toolbarRegistry;
        _toolbarContext = toolbarContext;
        _ocrController = ocrController;
        _annotation = annotation;
        _getSelection = getSelection;
        _getIsConfirmed = getIsConfirmed;
        _getFrozenLogicalWidth = getFrozenLogicalWidth;
        _getFrozenLogicalHeight = getFrozenLogicalHeight;
        _getCoordinates = getCoordinates;
        _getWindowTargetCandidateCount = getWindowTargetCandidateCount;
    }

    public void UpdateSelectionVisual()
    {
        var selection = _getSelection();
        _selectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(_selectionBorder, selection.X);
        Canvas.SetTop(_selectionBorder, selection.Y);
        _selectionBorder.Width = selection.Width;
        _selectionBorder.Height = selection.Height;

        Canvas.SetLeft(_annotationCanvas, selection.X);
        Canvas.SetTop(_annotationCanvas, selection.Y);
        _annotationCanvas.Width = selection.Width;
        _annotationCanvas.Height = selection.Height;
        _annotationCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, selection.Width, selection.Height)
        };
        _annotationCanvas.IsHitTestVisible = !_ocrController.IsActive
            && (_annotation.Tool.HasValue || !_annotation.IsEmpty);

        _ocrController.PositionOverlay(selection);

        UpdateDim();
        UpdateHandles();
        UpdateSizeLabel();
        UpdateToolbarPosition();
    }

    public void ApplySelectionBorderStyle(bool isWindowPreview)
    {
        _selectionBorder.Stroke = new SolidColorBrush(isWindowPreview
            ? Windows.UI.Color.FromArgb(0xFF, 0x36, 0x92, 0xFF)
            : Colors.White);
        _selectionBorder.StrokeThickness = isWindowPreview ? 2.25 : 1.75;
    }

    public void UpdateDim()
    {
        double w = _getFrozenLogicalWidth();
        double h = _getFrozenLogicalHeight();
        var selection = _getSelection();

        if (selection.IsEmpty)
        {
            _dimTop.Width = w; _dimTop.Height = h;
            Canvas.SetLeft(_dimTop, 0); Canvas.SetTop(_dimTop, 0);
            _dimBottom.Width = 0; _dimBottom.Height = 0;
            _dimLeft.Width = 0; _dimLeft.Height = 0;
            _dimRight.Width = 0; _dimRight.Height = 0;
            return;
        }

        var constrained = new LRect(
            selection.X,
            selection.Y,
            selection.Width,
            selection.Height)
            .IntersectedWithBounds(w, h);
        double sx = constrained.X, sy = constrained.Y;
        double sw = constrained.W, sh = constrained.H;
        _dimTop.Width = w; _dimTop.Height = sy;
        Canvas.SetLeft(_dimTop, 0); Canvas.SetTop(_dimTop, 0);
        _dimBottom.Width = w; _dimBottom.Height = Math.Max(0, h - sy - sh);
        Canvas.SetLeft(_dimBottom, 0); Canvas.SetTop(_dimBottom, sy + sh);
        _dimLeft.Width = sx; _dimLeft.Height = sh;
        Canvas.SetLeft(_dimLeft, 0); Canvas.SetTop(_dimLeft, sy);
        _dimRight.Width = Math.Max(0, w - sx - sw); _dimRight.Height = sh;
        Canvas.SetLeft(_dimRight, sx + sw); Canvas.SetTop(_dimRight, sy);
    }

    public void UpdateHandles()
    {
        if (!_getIsConfirmed())
        {
            foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
            return;
        }

        var selection = _getSelection();
        var lRect = new LRect(selection.X, selection.Y, selection.Width, selection.Height);
        for (int i = 0; i < 8; i++)
        {
            var pos = HandleOrder[i].PointIn(lRect);
            _handles[i].Visibility = Visibility.Visible;
            Canvas.SetLeft(_handles[i], pos.X - OverlayVisualTree.HandleSize / 2);
            Canvas.SetTop(_handles[i], pos.Y - OverlayVisualTree.HandleSize / 2);
        }
    }

    public void UpdateSizeLabel()
    {
        var selection = _getSelection();
        if (selection.IsEmpty) return;
        var coordinates = _getCoordinates();
        var pixelSize = coordinates.ToDisplaySize(new SelectionRect(
            selection.X,
            selection.Y,
            selection.Width,
            selection.Height));
        string cycleHint = !_getIsConfirmed() && _getWindowTargetCandidateCount() > 1
            ? "  ·  Tab 切换"
            : "";
        _sizeLabelText.Text = $"{pixelSize.Width} × {pixelSize.Height}{cycleHint}";
        double labelY = selection.Y >= 36 ? selection.Y - 28 : selection.Y + 8;
        double labelX = Math.Clamp(selection.X, 4, Math.Max(4, _getFrozenLogicalWidth() - 170));
        Canvas.SetLeft(_sizeLabel, labelX);
        Canvas.SetTop(_sizeLabel, labelY);
    }

    public void UpdateToolbarPosition()
    {
        var selection = _getSelection();
        if (selection.IsEmpty || !_getIsConfirmed()) return;
        double canvasW = _getFrozenLogicalWidth();
        double canvasH = _getFrozenLogicalHeight();
        var layout = _toolbar.Update(
            _toolbarRegistry,
            _toolbarContext,
            new ToolbarRect(0, 0, canvasW, canvasH),
            new ToolbarRect(selection.X, selection.Y, selection.Width, selection.Height));

        Canvas.SetLeft(_toolbar, layout.X);
        Canvas.SetTop(_toolbar, layout.Y);
    }

    public ResizeHandle? HitTestHandle(Point pos)
    {
        var selection = _getSelection();
        var lRect = new LRect(selection.X, selection.Y, selection.Width, selection.Height);
        foreach (var handle in HandleOrder)
        {
            var hPos = handle.PointIn(lRect);
            double dx = pos.X - hPos.X;
            double dy = pos.Y - hPos.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= 12)
                return handle;
        }
        return null;
    }
}
