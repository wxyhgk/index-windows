using Windows.Foundation;
using Index.Annotation;
using Index.Capture;
using Index.Platform;
using Index.Toolbar;

namespace Index.UI.Editor;

/// <summary>
/// 覆盖层确认/命令路由：ConfirmSelection、工具栏命令、复用上次选区。
/// 从 OverlayWindow 拆出，减少主窗口的命令逻辑。
/// </summary>
internal sealed class OverlayCaptureCommand
{
    private const double MinSelectionSize = 5;
    private readonly AnnotationState _annotation;
    private readonly OverlayWindowTargetController _windowTargetController;

    private readonly Func<CaptureDisplayIdentity?> _getSnapshotIdentity;
    private readonly Func<CaptureCoordinateMapper> _getCoordinates;
    private readonly Func<Rect> _getSelection;
    private readonly Func<SelectionController?> _getSelectionController;
    private readonly Func<CaptureSelection?> _getPreviousSelection;
    private readonly Func<Action<CaptureDecision>?> _getCaptureRequested;
    private readonly Func<bool> _getCloseOnCapture;
    private readonly Action _dismiss;
    private readonly Action _syncSelectionFromController;
    private readonly Action _enterConfirmedState;
    private readonly Action _requestCancel;

    public OverlayCaptureCommand(
        AnnotationState annotation,
        OverlayWindowTargetController windowTargetController,
        Func<CaptureDisplayIdentity?> getSnapshotIdentity,
        Func<CaptureCoordinateMapper> getCoordinates,
        Func<Rect> getSelection,
        Func<SelectionController?> getSelectionController,
        Func<CaptureSelection?> getPreviousSelection,
        Func<Action<CaptureDecision>?> getCaptureRequested,
        Func<bool> getCloseOnCapture,
        Action dismiss,
        Action syncSelectionFromController,
        Action enterConfirmedState,
        Action requestCancel)
    {
        _annotation = annotation;
        _windowTargetController = windowTargetController;
        _getSnapshotIdentity = getSnapshotIdentity;
        _getCoordinates = getCoordinates;
        _getSelection = getSelection;
        _getSelectionController = getSelectionController;
        _getPreviousSelection = getPreviousSelection;
        _getCaptureRequested = getCaptureRequested;
        _getCloseOnCapture = getCloseOnCapture;
        _dismiss = dismiss;
        _syncSelectionFromController = syncSelectionFromController;
        _enterConfirmedState = enterConfirmedState;
        _requestCancel = requestCancel;
    }

    public void ConfirmSelection(string actionId)
    {
        if (_getSnapshotIdentity() is not { } snapshotIdentity)
            throw new InvalidOperationException("覆盖窗口尚未绑定显示器快照。");

        var coordinates = _getCoordinates();
        var selection = _getSelection();
        var crop = coordinates.ToCropRect(new SelectionRect(
            selection.X,
            selection.Y,
            selection.Width,
            selection.Height));

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
                    new LRect(0, 0, selection.Width, selection.Height),
                    coordinates.ScaleX,
                    coordinates.ScaleY)
            },
            FindTargetWindowHandle());

        var handler = _getCaptureRequested();
        if (_getCloseOnCapture())
            _dismiss();
        handler?.Invoke(decision);
    }

    public void PerformToolbarCommand(string commandID)
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
                _requestCancel();
                break;
        }
    }

    public bool IsToolbarCommandEnabled(string commandId) =>
        commandId != ToolbarCommandIds.HighResolution4K
        || FindTargetWindowHandle() != nint.Zero;

    private nint FindTargetWindowHandle()
    {
        var selection = _getSelection();
        if (selection.IsEmpty)
            return nint.Zero;
        return _windowTargetController.ResolveCaptureHandle(
            new SelectionRect(
                selection.X,
                selection.Y,
                selection.Width,
                selection.Height),
            _getCoordinates());
    }

    public bool ApplyPreviousSelection()
    {
        if (_getPreviousSelection() is not { } previous
            || _getSnapshotIdentity() is not { } identity
            || !string.Equals(previous.Display.DisplayId, identity.DisplayId, StringComparison.OrdinalIgnoreCase))
            return false;

        var coordinates = _getCoordinates();
        var logical = coordinates.PixelToLogical(new SelectionRect(
            previous.X, previous.Y, previous.Width, previous.Height));

        if (logical.Width < MinSelectionSize || logical.Height < MinSelectionSize)
            return false;

        if (_getSelectionController() is not { } controller)
            return false;

        controller.SetSelection(logical);
        _syncSelectionFromController();
        _windowTargetController.ClearCaptureTarget();
        _windowTargetController.ClearPressedTarget();
        _enterConfirmedState();
        return true;
    }
}
