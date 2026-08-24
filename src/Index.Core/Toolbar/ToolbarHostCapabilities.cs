namespace Index.Toolbar;

/// <summary>不产生标注图层、但临时接管画布交互的宿主模式。</summary>
public enum ToolbarHostMode
{
    LiveText,
    SelectionAI
}

public sealed record ToolbarHostModeCapability(
    Func<bool> IsActive,
    Action Activate,
    Action Deactivate);

/// <summary>
/// Overlay、钉图等宿主按需注入能力；控件不反向依赖具体窗口。
/// 同一时间只允许一个临时画布模式激活。
/// </summary>
public sealed class ToolbarHostCapabilities
{
    public static ToolbarHostCapabilities None { get; } = new();

    private readonly IReadOnlyDictionary<ToolbarHostMode, ToolbarHostModeCapability> _modes;

    public ToolbarHostCapabilities(
        IReadOnlyDictionary<ToolbarHostMode, ToolbarHostModeCapability>? modes = null)
    {
        _modes = modes ?? new Dictionary<ToolbarHostMode, ToolbarHostModeCapability>();
    }

    public bool Supports(ToolbarHostMode mode) => _modes.ContainsKey(mode);

    public bool IsActive(ToolbarHostMode mode)
        => _modes.TryGetValue(mode, out var capability) && capability.IsActive();

    public bool HasActiveMode => _modes.Values.Any(capability => capability.IsActive());

    public void ToggleExclusive(ToolbarHostMode mode)
    {
        if (!_modes.TryGetValue(mode, out var target)) return;
        bool wasActive = target.IsActive();
        DeactivateAll();
        if (!wasActive) target.Activate();
    }

    public void DeactivateAll()
    {
        foreach (var capability in _modes.Values.Where(capability => capability.IsActive()))
            capability.Deactivate();
    }
}
