namespace Index.Toolbar;

/// <summary>
/// 工具栏控件的唯一聚合点；按 scope、显隐、分组和顺序返回稳定结果。
/// </summary>
public sealed class ToolbarRegistry
{
    private readonly List<IToolbarControl> _controls = new();

    public void Register(IToolbarControl control)
    {
        _controls.RemoveAll(existing => existing.Id == control.Id);
        _controls.Add(control);
    }

    public IReadOnlyList<IToolbarControl> ControlsFor(ToolbarContext context)
        => _controls
            .Where(control => control.Scopes.Contains(context.Scope) && control.IsVisible(context))
            .OrderBy(control => control.Group)
            .ThenBy(control => control.Order)
            .ToArray();
}

public static class BuiltinToolbarControls
{
    /// <summary>
    /// 骨架阶段只登记已经接通的动作。标注、历史与更多动作后续各自注册，宿主不变。
    /// </summary>
    public static void RegisterCaptureDefaults(
        ToolbarRegistry registry)
    {
        registry.Register(new LiveTextToolbarControl());
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Pin, "📌", "钉图", ToolbarGroup.Actions, 0, false, ToolbarScope.Capture));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Copy, "⧉", "复制", ToolbarGroup.Actions, 1, false, ToolbarScope.Capture));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.HighResolution4K,
            "4K",
            "直接保存 4K 高 DPI 截图",
            ToolbarGroup.Actions,
            2,
            false,
            ToolbarScope.Capture));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Complete, "✓", "完成", ToolbarGroup.Actions, 3, false, ToolbarScope.Capture));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Cancel, "×", "取消", ToolbarGroup.Actions, 4, false, ToolbarScope.Capture));
    }

    public static void RegisterPinnedDefaults(ToolbarRegistry registry)
    {
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.CopyText,
            "文",
            "复制文字",
            ToolbarGroup.Actions,
            0,
            false,
            ToolbarScope.Pinned));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Copy, "⧉", "复制图片", ToolbarGroup.Actions, 1, false, ToolbarScope.Pinned));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Save, "↓", "保存到桌面", ToolbarGroup.Actions, 2, false, ToolbarScope.Pinned));
        registry.Register(new CommandToolbarControl(
            ToolbarCommandIds.Close, "×", "关闭", ToolbarGroup.Actions, 3, false, ToolbarScope.Pinned));
    }
}
