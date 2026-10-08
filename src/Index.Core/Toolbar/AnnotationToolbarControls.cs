using Index.Annotation;

namespace Index.Toolbar;

/// <summary>
/// Stable IDs for annotation controls. Hosts may use these for focus restoration or
/// keyboard routing without knowing a concrete control type.
/// </summary>
public static class AnnotationToolbarControlIds
{
    public const string Pointer = "tool.pointer";
    public const string Shape = "tool.shape";
    public const string MoreTools = "tool.more";
    public const string Undo = "history.undo";
    public const string Redo = "history.redo";

    public static string Tool(AnnotationTool tool) => $"tool.{tool.Id()}";
}

/// <summary>矩形与椭圆共用一个紧凑入口，具体形状由宿主弹出菜单选择。</summary>
public sealed class ShapeAnnotationToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    public static readonly AnnotationTool[] Tools =
        { AnnotationTool.Rect, AnnotationTool.Ellipse };

    public override string Id => AnnotationToolbarControlIds.Shape;
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.Tools;
    public override int Order => 1;
    public override string Glyph => "□";
    public override string Label => "形状";

    public AnnotationTool ActiveTool(ToolbarContext context)
        => context.Annotation.Tool == AnnotationTool.Ellipse
            ? AnnotationTool.Ellipse
            : AnnotationTool.Rect;

    public override bool IsSelected(ToolbarContext context)
        => context.Annotation.Tool is AnnotationTool.Rect or AnnotationTool.Ellipse
           && !context.Capabilities.HasActiveMode;

    // 打开原生菜单由 WinUI 宿主负责。
    public override void Activate(ToolbarContext context) { }

    public void SelectTool(AnnotationTool tool, ToolbarContext context)
    {
        if (!Tools.Contains(tool))
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Tool is not a shape variant.");

        context.Annotation.EndTextEditing();
        context.Capabilities.DeactivateAll();
        context.Annotation.Tool = tool;
    }
}

/// <summary>
/// Menu entry for tools that are implemented but intentionally not pinned to the
/// compact capture toolbar. The WinUI host renders the menu; selection remains a
/// domain-neutral toolbar operation.
/// </summary>
public sealed class MoreAnnotationToolsToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    public IReadOnlyList<IAnnotationToolDescriptor> Descriptors { get; }

    public override string Id => AnnotationToolbarControlIds.MoreTools;
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.Tools;
    public override int Order => 10_000;
    public override string Glyph => "\u2026";
    public override string Label => "更多工具";

    public MoreAnnotationToolsToolbarControl(
        IEnumerable<IAnnotationToolDescriptor> descriptors,
        bool allowText = false,
        bool allowCompositing = false)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        Descriptors = descriptors
            .Where(descriptor => !descriptor.IsPinnedToBar
                && IsAvailable(descriptor.Tool, allowText, allowCompositing))
            .ToArray();
    }

    public override bool IsVisible(ToolbarContext context) => Descriptors.Count > 0;

    // Opening the native menu is a WinUI concern; see ToolbarView.
    public override void Activate(ToolbarContext context) { }

    public void SelectTool(AnnotationTool tool, ToolbarContext context)
    {
        if (!Descriptors.Any(descriptor => descriptor.Tool == tool))
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Tool is not in this menu.");

        context.Annotation.EndTextEditing();
        context.Capabilities.DeactivateAll();
        context.Annotation.Tool = tool;
    }

    internal static bool IsAvailable(
        AnnotationTool tool,
        bool allowText = false,
        bool allowCompositing = false)
        => (allowCompositing || tool is not AnnotationTool.Pixelate and not AnnotationTool.Crop)
           && (allowText || tool is not AnnotationTool.Text);
}

/// <summary>
/// A registered annotation tool. The descriptor remains the source of truth for
/// ordering and whether a tool is permanently pinned to the toolbar.
/// </summary>
public sealed class AnnotationToolToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    private readonly IAnnotationToolDescriptor? _descriptor;
    private readonly int _order;
    private readonly bool _allowText;
    private readonly bool _allowCompositing;

    /// <summary><see langword="null"/> represents pointer/select mode.</summary>
    public AnnotationTool? Tool => _descriptor?.Tool;

    public override string Id => Tool.HasValue
        ? AnnotationToolbarControlIds.Tool(Tool.Value)
        : AnnotationToolbarControlIds.Pointer;
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.Tools;
    public override int Order => _order;
    public override string Glyph => ToolGlyph(Tool);
    public override string Label => Tool?.Title() ?? "指针";

    public AnnotationToolToolbarControl(
        IAnnotationToolDescriptor? descriptor,
        int order,
        bool allowText = false,
        bool allowCompositing = false)
    {
        _descriptor = descriptor;
        _order = order;
        _allowText = allowText;
        _allowCompositing = allowCompositing;
    }

    public override bool IsVisible(ToolbarContext context)
    {
        // Hosts opt into text/compositing only after supplying their required editor
        // surfaces. Capture and pin keep unsupported tools hidden.
        if (Tool.HasValue && !MoreAnnotationToolsToolbarControl.IsAvailable(
                Tool.Value,
                _allowText,
                _allowCompositing))
            return false;
        return _descriptor is null || _descriptor.IsPinnedToBar || context.Annotation.Tool == Tool;
    }

    public override bool IsSelected(ToolbarContext context)
        => context.Annotation.Tool == Tool && !context.Capabilities.HasActiveMode;

    public override void Activate(ToolbarContext context)
    {
        context.Annotation.EndTextEditing();
        context.Capabilities.DeactivateAll();
        context.Annotation.Tool = Tool;
    }

    private static string ToolGlyph(AnnotationTool? tool) => tool switch
    {
        null => "↖",
        AnnotationTool.Rect => "□",
        AnnotationTool.Ellipse => "○",
        AnnotationTool.Arrow => "↗",
        AnnotationTool.Text => "T",
        AnnotationTool.Highlight => "▰",
        AnnotationTool.Pixelate => "▦",
        AnnotationTool.Line => "╱",
        AnnotationTool.Crop => "⌗",
        AnnotationTool.Counter => "①",
        AnnotationTool.Spotlight => "◉",
        AnnotationTool.Dimension => "↔",
        _ => "•"
    };
}

/// <summary>
/// Undo/redo controls keep their slots visible while reflecting availability from
/// the annotation history, so action controls do not move as history changes.
/// </summary>
public sealed class AnnotationHistoryToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    private readonly bool _isUndo;

    public override string Id => _isUndo
        ? AnnotationToolbarControlIds.Undo
        : AnnotationToolbarControlIds.Redo;
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.History;
    public override int Order => _isUndo ? 0 : 1;
    public override string Glyph => _isUndo ? "↶" : "↷";
    public override string Label => _isUndo ? "撤销" : "重做";

    public AnnotationHistoryToolbarControl(bool isUndo)
    {
        _isUndo = isUndo;
    }

    public override bool IsEnabled(ToolbarContext context)
        => _isUndo ? context.Annotation.CanUndo : context.Annotation.CanRedo;

    public override void Activate(ToolbarContext context)
    {
        if (_isUndo)
            context.Annotation.Undo();
        else
            context.Annotation.Redo();
    }
}

/// <summary>
/// Registration entry point for the capture annotation toolbar. Tool order and
/// pinned visibility are derived from <see cref="ToolRegistry"/> descriptors.
/// </summary>
public static class AnnotationToolbarControls
{
    public static void RegisterCaptureAnnotationDefaults(ToolbarRegistry registry)
        => RegisterAnnotationDefaults(
            registry,
            allowText: false,
            allowCompositing: false);

    /// <summary>The editor exposes text because its inspector owns text input.</summary>
    public static void RegisterEditorAnnotationDefaults(ToolbarRegistry registry)
        => RegisterAnnotationDefaults(
            registry,
            allowText: true,
            allowCompositing: true);

    private static void RegisterAnnotationDefaults(
        ToolbarRegistry registry,
        bool allowText,
        bool allowCompositing)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new AnnotationToolToolbarControl(
            descriptor: null,
            order: 0,
            allowText: allowText,
            allowCompositing: allowCompositing));
        registry.Register(new ShapeAnnotationToolbarControl());

        for (int index = 0; index < ToolRegistry.Descriptors.Count; index++)
        {
            if (ToolRegistry.Descriptors[index].Tool is AnnotationTool.Rect or AnnotationTool.Ellipse)
                continue;
            registry.Register(new AnnotationToolToolbarControl(
                ToolRegistry.Descriptors[index],
                order: index + 1,
                allowText: allowText,
                allowCompositing: allowCompositing));
        }

        registry.Register(new MoreAnnotationToolsToolbarControl(
            ToolRegistry.Descriptors,
            allowText,
            allowCompositing));

        registry.Register(new AnnotationHistoryToolbarControl(isUndo: true));
        registry.Register(new AnnotationHistoryToolbarControl(isUndo: false));
    }
}
