using Index.Annotation;

namespace Index.Toolbar;

/// <summary>Stable IDs for controls shown on the annotation style row.</summary>
public static class AnnotationStyleToolbarControlIds
{
    public static string Shape(AnnotationTool tool) => $"shape.{tool.Id()}";
    public static string Color(int index) => $"style.color.{index}";
    public static string Parameter(ToolStyleAxis axis, int index)
        => $"style.{ToolStyleAxisDescriptor.DescriptorFor(axis).CodingKey}.{index}";
}

/// <summary>
/// PixPin-style shape variants: rectangle and ellipse share one top-level shape
/// tool and expose their concrete form at the start of the style row.
/// </summary>
public sealed class ShapeVariantToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    public AnnotationTool Tool { get; }

    public override string Id => AnnotationStyleToolbarControlIds.Shape(Tool);
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.Style;
    public override int Order => Tool == AnnotationTool.Rect ? -20 : -19;
    public override string Glyph => Tool == AnnotationTool.Rect ? "□" : "○";
    public override string Label => Tool.Title();

    public ShapeVariantToolbarControl(AnnotationTool tool)
    {
        if (tool is not AnnotationTool.Rect and not AnnotationTool.Ellipse)
            throw new ArgumentOutOfRangeException(nameof(tool), tool, "Only rectangle and ellipse are shape variants.");
        Tool = tool;
    }

    public override bool IsVisible(ToolbarContext context)
        => context.Annotation.StyleTool is AnnotationTool.Rect or AnnotationTool.Ellipse;

    public override bool IsSelected(ToolbarContext context)
        => context.Annotation.Tool == Tool;

    public override void Activate(ToolbarContext context)
    {
        context.Annotation.EndTextEditing();
        context.Capabilities.DeactivateAll();
        context.Annotation.Tool = Tool;
    }
}

/// <summary>A palette entry, registered directly from <see cref="AnnotationState.Palette"/>.</summary>
public sealed class AnnotationColorToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    public int Index { get; }
    public LColor Color => AnnotationState.Palette[Index];

    public override string Id => AnnotationStyleToolbarControlIds.Color(Index);
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.Style;
    public override int Order => Index;
    public override string Glyph => PaletteGlyph(Index);
    public override string Label => $"颜色 {Index + 1}";

    public AnnotationColorToolbarControl(int index)
    {
        if (index < 0 || index >= AnnotationState.Palette.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
    }

    public override bool IsVisible(ToolbarContext context)
        => context.Annotation.StyleAxes.Contains(ToolStyleAxis.Color);

    public override bool IsSelected(ToolbarContext context)
        => context.Annotation.CurrentStyle.ColorIndex == Index;

    public override double PreferredWidth(ToolbarContext context) => ToolbarLayout.StyleButtonWidth;

    public override void Activate(ToolbarContext context)
    {
        context.Annotation.SetStyleIndex(Index, ToolStyleAxis.Color);
        context.Annotation.ApplyColorToSelection();
    }

    private static string PaletteGlyph(int index) => index switch
    {
        0 => "●",
        1 => "◆",
        2 => "■",
        3 => "○",
        _ => "●"
    };
}

/// <summary>
/// One discrete step of a numeric style axis. Axis visibility, ordering, labels,
/// and step counts all come from <see cref="ToolStyleAxisDescriptor"/>.
/// </summary>
public sealed class AnnotationStyleParameterToolbarControl : ToolbarControlBase
{
    private static readonly IReadOnlySet<ToolbarScope> CaptureScopes =
        new HashSet<ToolbarScope> { ToolbarScope.Capture };

    public ToolStyleAxisDescriptor Descriptor { get; }
    public int Index { get; }
    public double Value => Descriptor.Steps[Index];

    public override string Id => AnnotationStyleToolbarControlIds.Parameter(Descriptor.Axis, Index);
    public override IReadOnlySet<ToolbarScope> Scopes => CaptureScopes;
    public override ToolbarGroup Group => ToolbarGroup.Style;
    public override int Order => Descriptor.Order + Index;
    public override string Glyph => ParameterGlyph(Descriptor.Axis, Index);
    public override string Label => $"{Descriptor.Title} {Index + 1}";

    public AnnotationStyleParameterToolbarControl(ToolStyleAxisDescriptor descriptor, int index)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Axis == ToolStyleAxis.Color)
            throw new ArgumentException("Color uses AnnotationColorToolbarControl.", nameof(descriptor));
        if (index < 0 || index >= descriptor.Steps.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        Descriptor = descriptor;
        Index = index;
    }

    public override bool IsVisible(ToolbarContext context)
        => context.Annotation.StyleAxes.Contains(Descriptor.Axis);

    public override bool IsSelected(ToolbarContext context)
        => context.Annotation.CurrentStyle.IndexFor(Descriptor.Axis) == Index;

    public override double PreferredWidth(ToolbarContext context) => ToolbarLayout.StyleButtonWidth;

    public override void Activate(ToolbarContext context)
    {
        context.Annotation.SetStyleIndex(Index, Descriptor.Axis);
        context.Annotation.ApplyStyleToSelection(Descriptor.Axis);
    }

    private static string ParameterGlyph(ToolStyleAxis axis, int index) => axis switch
    {
        ToolStyleAxis.Width => index switch { 0 => "·", 1 => "•", _ => "●" },
        ToolStyleAxis.FontSize => index switch { 0 => "ᴀ", 1 => "A", _ => "Ａ" },
        ToolStyleAxis.Opacity => index switch { 0 => "◌", 1 => "◍", _ => "●" },
        ToolStyleAxis.BlockSize => index switch { 0 => "⣿", 1 => "▦", _ => "▣" },
        ToolStyleAxis.Dim => index switch { 0 => "◔", 1 => "◑", _ => "●" },
        _ => "•"
    };
}

/// <summary>Registration entry point for the capture toolbar's second row.</summary>
public static class AnnotationStyleToolbarControls
{
    public static void RegisterCaptureStyleDefaults(ToolbarRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new ShapeVariantToolbarControl(AnnotationTool.Rect));
        registry.Register(new ShapeVariantToolbarControl(AnnotationTool.Ellipse));

        for (int index = 0; index < AnnotationState.Palette.Length; index++)
            registry.Register(new AnnotationColorToolbarControl(index));

        foreach (var descriptor in ToolStyleAxisDescriptor.All)
        {
            if (descriptor.Axis == ToolStyleAxis.Color)
                continue;

            for (int index = 0; index < descriptor.Steps.Length; index++)
                registry.Register(new AnnotationStyleParameterToolbarControl(descriptor, index));
        }
    }
}
