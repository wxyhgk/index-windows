using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Index.Capture;
using Index.Toolbar;

namespace Index.UI.Editor;

/// <summary>
/// 覆盖层键盘处理：路由 Enter/Esc/Tab/R/Ctrl+C/Shift+C 按键。
/// 从 OverlayWindow 拆出，减少主窗口的键盘逻辑。
/// </summary>
internal sealed class OverlayKeyboardHandler
{
    private readonly OverlayOcrController _ocrController;
    private readonly Func<bool> _getIsConfirmed;
    private readonly Func<Point> _getLastPointerPosition;
    private readonly Func<CaptureCoordinateMapper> _getCoordinates;
    private readonly Func<int, bool> _cycleWindowTarget;
    private readonly Func<SelectionPoint, CaptureCoordinateMapper, bool> _hasTargetAt;
    private readonly Action _requestCancel;
    private readonly Action<string> _confirmSelection;
    private readonly Func<bool> _applyPreviousSelection;

    public OverlayKeyboardHandler(
        OverlayOcrController ocrController,
        Func<bool> getIsConfirmed,
        Func<Point> getLastPointerPosition,
        Func<CaptureCoordinateMapper> getCoordinates,
        Func<int, bool> cycleWindowTarget,
        Func<SelectionPoint, CaptureCoordinateMapper, bool> hasTargetAt,
        Action requestCancel,
        Action<string> confirmSelection,
        Func<bool> applyPreviousSelection)
    {
        _ocrController = ocrController;
        _getIsConfirmed = getIsConfirmed;
        _getLastPointerPosition = getLastPointerPosition;
        _getCoordinates = getCoordinates;
        _cycleWindowTarget = cycleWindowTarget;
        _hasTargetAt = hasTargetAt;
        _requestCancel = requestCancel;
        _confirmSelection = confirmSelection;
        _applyPreviousSelection = applyPreviousSelection;
    }

    public void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shiftDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        bool controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.C when controlDown && _ocrController.IsActive:
                _ocrController.CopySelected();
                e.Handled = true;
                break;

            case VirtualKey.C when shiftDown && _getIsConfirmed():
                _ocrController.CopyAll();
                e.Handled = true;
                break;

            case VirtualKey.Tab:
                bool cycled = _cycleWindowTarget(shiftDown ? -1 : 1);
                bool hasTarget = !_getIsConfirmed()
                    && _hasTargetAt(
                        new SelectionPoint(
                            _getLastPointerPosition().X,
                            _getLastPointerPosition().Y),
                        _getCoordinates());
                if (cycled || hasTarget)
                    e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (_ocrController.IsActive && _ocrController.HasSelection)
                    _ocrController.ClearSelection();
                else if (_ocrController.IsActive)
                    _ocrController.Deactivate();
                else
                    _requestCancel();
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                if (_getIsConfirmed())
                {
                    _confirmSelection(ToolbarCommandIds.Complete);
                    e.Handled = true;
                }
                break;

            case VirtualKey.R:
                if (_applyPreviousSelection())
                    e.Handled = true;
                break;
        }
    }
}
