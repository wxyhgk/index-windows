using Index.Annotation;

namespace Index.Toolbar;

public enum ToolbarScope
{
    Capture,
    Pinned
}

public enum ToolbarGroup
{
    Tools,
    Style,
    History,
    Actions
}

/// <summary>
/// 工具栏宿主提供的最小上下文。控件不认识 OverlayWindow，也不直接保存文件。
/// </summary>
public sealed class ToolbarContext
{
    public AnnotationState Annotation { get; }
    public ToolbarScope Scope { get; }
    public Action<string> Perform { get; }
    public ToolbarHostCapabilities Capabilities { get; }
    public Func<string, bool> IsActionExecuting { get; }
    public Func<string, bool> IsActionEnabled { get; }

    public ToolbarContext(
        AnnotationState annotation,
        ToolbarScope scope,
        Action<string> perform,
        ToolbarHostCapabilities? capabilities = null,
        Func<string, bool>? isActionExecuting = null,
        Func<string, bool>? isActionEnabled = null)
    {
        Annotation = annotation;
        Scope = scope;
        Perform = perform;
        Capabilities = capabilities ?? ToolbarHostCapabilities.None;
        IsActionExecuting = isActionExecuting ?? (_ => false);
        IsActionEnabled = isActionEnabled ?? (_ => true);
    }
}

/// <summary>
/// UI 无关的工具栏控件契约。后续新增工具或动作只需实现并注册。
/// </summary>
public interface IToolbarControl
{
    string Id { get; }
    IReadOnlySet<ToolbarScope> Scopes { get; }
    ToolbarGroup Group { get; }
    int Order { get; }
    string Glyph { get; }
    string Label { get; }
    bool ShowsLabel { get; }

    bool IsVisible(ToolbarContext context);
    bool IsEnabled(ToolbarContext context);
    bool IsSelected(ToolbarContext context);
    bool IsBusy(ToolbarContext context);
    double PreferredWidth(ToolbarContext context);
    void Activate(ToolbarContext context);
}

public abstract class ToolbarControlBase : IToolbarControl
{
    public abstract string Id { get; }
    public abstract IReadOnlySet<ToolbarScope> Scopes { get; }
    public abstract ToolbarGroup Group { get; }
    public abstract int Order { get; }
    public abstract string Glyph { get; }
    public abstract string Label { get; }
    public virtual bool ShowsLabel => false;

    public virtual bool IsVisible(ToolbarContext context) => true;
    public virtual bool IsEnabled(ToolbarContext context) => true;
    public virtual bool IsSelected(ToolbarContext context) => false;
    public virtual bool IsBusy(ToolbarContext context) => false;
    public virtual double PreferredWidth(ToolbarContext context)
        => ShowsLabel ? 72 : ToolbarLayout.IconButtonWidth;
    public abstract void Activate(ToolbarContext context);
}

public sealed class CommandToolbarControl : ToolbarControlBase
{
    private readonly IReadOnlySet<ToolbarScope> _scopes;

    public override string Id { get; }
    public override IReadOnlySet<ToolbarScope> Scopes => _scopes;
    public override ToolbarGroup Group { get; }
    public override int Order { get; }
    public override string Glyph { get; }
    public override string Label { get; }
    public override bool ShowsLabel { get; }

    public CommandToolbarControl(
        string id,
        string glyph,
        string label,
        ToolbarGroup group,
        int order,
        bool showsLabel,
        params ToolbarScope[] scopes)
    {
        Id = id;
        Glyph = glyph;
        Label = label;
        Group = group;
        Order = order;
        ShowsLabel = showsLabel;
        _scopes = scopes.ToHashSet();
    }

    public override bool IsEnabled(ToolbarContext context) =>
        context.IsActionEnabled(Id) && !context.IsActionExecuting(Id);
    public override bool IsBusy(ToolbarContext context) => context.IsActionExecuting(Id);
    public override void Activate(ToolbarContext context) => context.Perform(Id);
}

public static class ToolbarCommandIds
{
    public const string Pin = "pin";
    public const string Complete = "complete";
    public const string Save = "save";
    public const string Copy = "copy";
    public const string Close = "close";
    public const string Cancel = "action.cancel";
    public const string HighResolution4K = "capture.4k";
}
