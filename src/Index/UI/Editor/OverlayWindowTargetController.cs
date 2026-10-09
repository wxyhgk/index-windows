using Index.Capture;
using Index.Platform;

namespace Index.UI.Editor;

/// <summary>
/// 覆盖层窗口目标导航：管理 WindowTargetNavigator 生命周期、
/// 按下/捕获目标状态和像素边缘检测请求。
/// 从 OverlayWindow 拆出，减少主窗口的状态字段。
/// </summary>
internal sealed class OverlayWindowTargetController
{
    private WindowTargetNavigator? _navigator;
    private WindowSelectionTarget? _pressedTarget;
    private WindowSelectionTarget? _captureTarget;
    private bool _pixelEdgeDetectionRequested;

    public event Action? PixelEdgeDetectionRequested;

    public WindowSelectionTarget? PressedTarget => _pressedTarget;
    public WindowSelectionTarget? CaptureTarget => _captureTarget;
    public int CandidateCount => _navigator?.CandidateCount ?? 0;

    public void Initialize(
        IReadOnlyList<WindowSelectionTarget> targets,
        FrozenPixelEdgeDetector? detector,
        CaptureDisplayIdentity identity)
    {
        _navigator = new WindowTargetNavigator(targets, detector, identity);
        _pressedTarget = null;
        _captureTarget = null;
        _pixelEdgeDetectionRequested = false;
    }

    public void SetPixelEdgeDetector(FrozenPixelEdgeDetector? detector)
        => _navigator?.SetPixelEdgeDetector(detector);

    public WindowSelectionTarget? FindPressedTarget(
        SelectionPoint point,
        CaptureCoordinateMapper coords)
    {
        _pressedTarget = _navigator is { IsCurrentTargetExplicit: true }
            && _navigator.CurrentTarget is { } hovered
            && WindowTargetNavigator.Contains(hovered, point)
            ? hovered
            : _navigator?.FindPrimary(point, coords);
        return _pressedTarget;
    }

    public WindowSelectionTarget? PreviewAt(
        SelectionPoint point,
        CaptureCoordinateMapper coords)
        => _navigator?.PreviewAt(point, coords);

    public bool TryCycle(
        SelectionPoint point,
        int direction,
        CaptureCoordinateMapper coords,
        out WindowSelectionTarget? target)
    {
        target = null;
        return _navigator is not null
            && _navigator.TryCycle(point, direction, coords, out target);
    }

    public bool HasTargetAt(
        SelectionPoint point,
        CaptureCoordinateMapper coords)
        => _navigator?.HasTargetAt(point, coords) == true;

    public nint ResolveCaptureHandle(
        SelectionRect selection,
        CaptureCoordinateMapper coords)
        => _navigator?.ResolveCaptureHandle(_captureTarget, selection, coords) ?? nint.Zero;

    public void Reset()
    {
        _navigator?.Reset();
        _pressedTarget = null;
        _captureTarget = null;
    }

    public void ClearPressedTarget() => _pressedTarget = null;
    public void SetCaptureTarget(WindowSelectionTarget? target) => _captureTarget = target;
    public void ClearCaptureTarget() => _captureTarget = null;

    public void RequestPixelEdgeDetection()
    {
        if (_pixelEdgeDetectionRequested) return;
        _pixelEdgeDetectionRequested = true;
        PixelEdgeDetectionRequested?.Invoke();
    }
}
