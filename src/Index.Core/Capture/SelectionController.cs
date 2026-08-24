namespace Index.Capture;

/// <summary>A point in the capture canvas' logical coordinate space.</summary>
public readonly record struct SelectionPoint(double X, double Y);

/// <summary>A normalized rectangle in the capture canvas' logical coordinate space.</summary>
public readonly record struct SelectionRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static SelectionRect FromPoints(SelectionPoint first, SelectionPoint second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(second.X - first.X),
        Math.Abs(second.Y - first.Y));
}

public enum SelectionResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left
}

public enum SelectionInteraction
{
    None,
    Create,
    Move,
    Resize
}

/// <summary>
/// Platform-independent state machine for creating and adjusting a capture selection.
/// The controller owns interaction rollback; UI hosts only translate pointer events into
/// Begin/Update/End/Cancel calls.
/// </summary>
public sealed class SelectionController
{
    private SelectionPoint _interactionStart;
    private SelectionRect? _interactionStartSelection;
    private SelectionResizeHandle _resizeHandle;

    public SelectionController(
        SelectionRect canvasBounds,
        double minimumWidth,
        double minimumHeight,
        SelectionRect? initialSelection = null)
    {
        if (canvasBounds.Width <= 0 || canvasBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvasBounds), "Canvas bounds must have positive dimensions.");
        if (minimumWidth <= 0 || minimumWidth > canvasBounds.Width)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        if (minimumHeight <= 0 || minimumHeight > canvasBounds.Height)
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));

        CanvasBounds = canvasBounds;
        MinimumWidth = minimumWidth;
        MinimumHeight = minimumHeight;

        if (initialSelection is { } selection)
            ValidateSelection(selection, nameof(initialSelection));
        Selection = initialSelection;
    }

    public SelectionRect CanvasBounds { get; }
    public double MinimumWidth { get; }
    public double MinimumHeight { get; }
    public SelectionRect? Selection { get; private set; }
    public SelectionInteraction Interaction { get; private set; }
    public bool IsInteracting => Interaction != SelectionInteraction.None;

    public void SetSelection(SelectionRect? selection)
    {
        EnsureIdle();
        if (selection is { } value)
            ValidateSelection(value, nameof(selection));
        Selection = selection;
    }

    public bool BeginCreate(SelectionPoint point)
    {
        if (IsInteracting) return false;

        _interactionStartSelection = Selection;
        _interactionStart = ClampPoint(point);
        Selection = new SelectionRect(_interactionStart.X, _interactionStart.Y, 0, 0);
        Interaction = SelectionInteraction.Create;
        return true;
    }

    public bool BeginMove(SelectionPoint point)
    {
        if (IsInteracting || Selection is null) return false;

        _interactionStartSelection = Selection;
        _interactionStart = point;
        Interaction = SelectionInteraction.Move;
        return true;
    }

    public bool BeginResize(SelectionResizeHandle handle, SelectionPoint point)
    {
        if (IsInteracting || Selection is null) return false;

        _interactionStartSelection = Selection;
        _interactionStart = point;
        _resizeHandle = handle;
        Interaction = SelectionInteraction.Resize;
        return true;
    }

    public bool Update(SelectionPoint point)
    {
        if (!IsInteracting || _interactionStartSelection is null && Interaction != SelectionInteraction.Create)
            return false;

        Selection = Interaction switch
        {
            SelectionInteraction.Create => SelectionRect.FromPoints(_interactionStart, ClampPoint(point)),
            SelectionInteraction.Move => MoveSelection(point),
            SelectionInteraction.Resize => ResizeSelection(point),
            _ => Selection
        };
        return true;
    }

    /// <summary>
    /// Commits the active interaction. A creation smaller than the configured minimum is
    /// rejected and restores the selection that existed before BeginCreate.
    /// </summary>
    public bool End()
    {
        if (!IsInteracting) return false;

        bool valid = Selection is { } selection
            && selection.Width >= MinimumWidth
            && selection.Height >= MinimumHeight;
        if (!valid)
            Selection = _interactionStartSelection;

        ResetInteraction();
        return valid;
    }

    /// <summary>Aborts the active interaction and restores its exact starting selection.</summary>
    public bool Cancel()
    {
        if (!IsInteracting) return false;

        Selection = _interactionStartSelection;
        ResetInteraction();
        return true;
    }

    private SelectionRect MoveSelection(SelectionPoint point)
    {
        var original = _interactionStartSelection!.Value;
        double x = original.X + point.X - _interactionStart.X;
        double y = original.Y + point.Y - _interactionStart.Y;
        x = Math.Clamp(x, CanvasBounds.Left, CanvasBounds.Right - original.Width);
        y = Math.Clamp(y, CanvasBounds.Top, CanvasBounds.Bottom - original.Height);
        return new SelectionRect(x, y, original.Width, original.Height);
    }

    private SelectionRect ResizeSelection(SelectionPoint point)
    {
        var original = _interactionStartSelection!.Value;
        double dx = point.X - _interactionStart.X;
        double dy = point.Y - _interactionStart.Y;
        double left = original.Left;
        double top = original.Top;
        double right = original.Right;
        double bottom = original.Bottom;

        if (_resizeHandle is SelectionResizeHandle.TopLeft or SelectionResizeHandle.Left or SelectionResizeHandle.BottomLeft)
            left = Math.Clamp(original.Left + dx, CanvasBounds.Left, original.Right - MinimumWidth);
        if (_resizeHandle is SelectionResizeHandle.TopRight or SelectionResizeHandle.Right or SelectionResizeHandle.BottomRight)
            right = Math.Clamp(original.Right + dx, original.Left + MinimumWidth, CanvasBounds.Right);
        if (_resizeHandle is SelectionResizeHandle.TopLeft or SelectionResizeHandle.Top or SelectionResizeHandle.TopRight)
            top = Math.Clamp(original.Top + dy, CanvasBounds.Top, original.Bottom - MinimumHeight);
        if (_resizeHandle is SelectionResizeHandle.BottomLeft or SelectionResizeHandle.Bottom or SelectionResizeHandle.BottomRight)
            bottom = Math.Clamp(original.Bottom + dy, original.Top + MinimumHeight, CanvasBounds.Bottom);

        return new SelectionRect(left, top, right - left, bottom - top);
    }

    private SelectionPoint ClampPoint(SelectionPoint point) => new(
        Math.Clamp(point.X, CanvasBounds.Left, CanvasBounds.Right),
        Math.Clamp(point.Y, CanvasBounds.Top, CanvasBounds.Bottom));

    private void ValidateSelection(SelectionRect selection, string parameterName)
    {
        if (selection.Width < MinimumWidth || selection.Height < MinimumHeight)
            throw new ArgumentOutOfRangeException(parameterName, "Selection is smaller than the configured minimum.");
        if (selection.Left < CanvasBounds.Left || selection.Top < CanvasBounds.Top
            || selection.Right > CanvasBounds.Right || selection.Bottom > CanvasBounds.Bottom)
            throw new ArgumentOutOfRangeException(parameterName, "Selection must be inside the canvas bounds.");
    }

    private void EnsureIdle()
    {
        if (IsInteracting)
            throw new InvalidOperationException("The selection cannot be replaced during an active interaction.");
    }

    private void ResetInteraction()
    {
        Interaction = SelectionInteraction.None;
        _interactionStartSelection = null;
        _interactionStart = default;
        _resizeHandle = default;
    }
}
