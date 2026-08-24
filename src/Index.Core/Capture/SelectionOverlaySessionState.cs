namespace Index.Capture;

public enum SelectionOverlaySessionStatus
{
    Active,
    Completed,
    Canceled
}

/// <summary>
/// 多屏覆盖会话的纯状态机：维护活动屏幕与严格的一次性完成门。
/// 不依赖 WinUI，因此真实桌面窗口之外也能完整验证会话语义。
/// </summary>
public sealed class SelectionOverlaySessionState
{
    private readonly HashSet<string> _displayIds;

    public SelectionOverlaySessionState(IEnumerable<string> displayIds)
    {
        ArgumentNullException.ThrowIfNull(displayIds);
        _displayIds = displayIds.ToHashSet();
        if (_displayIds.Count == 0)
            throw new ArgumentException("覆盖会话至少需要一块显示器。", nameof(displayIds));
    }

    public SelectionOverlaySessionStatus Status { get; private set; }
        = SelectionOverlaySessionStatus.Active;

    public string? ActiveDisplayId { get; private set; }

    public bool IsTerminal => Status != SelectionOverlaySessionStatus.Active;

    public IReadOnlyList<string> Activate(string displayId)
    {
        EnsureKnownDisplay(displayId);
        if (IsTerminal) return Array.Empty<string>();

        ActiveDisplayId = displayId;
        return _displayIds
            .Where(candidate => candidate != displayId)
            .OrderBy(candidate => candidate)
            .ToArray();
    }

    public bool TryComplete(string displayId)
    {
        EnsureKnownDisplay(displayId);
        if (IsTerminal) return false;

        ActiveDisplayId = displayId;
        Status = SelectionOverlaySessionStatus.Completed;
        return true;
    }

    public bool TryCancel()
    {
        if (IsTerminal) return false;

        Status = SelectionOverlaySessionStatus.Canceled;
        return true;
    }

    private void EnsureKnownDisplay(string displayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        if (!_displayIds.Contains(displayId))
            throw new ArgumentException(
                "显示器不属于当前覆盖会话。",
                nameof(displayId));
    }
}
