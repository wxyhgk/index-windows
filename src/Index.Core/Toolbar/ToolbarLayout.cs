namespace Index.Toolbar;

public readonly record struct ToolbarRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public readonly record struct ToolbarSlot(ToolbarRect Frame, IToolbarControl? Control)
{
    public ToolbarRect HitFrame => new(
        Frame.X - ToolbarLayout.HitPadding,
        Frame.Y - ToolbarLayout.HitPadding,
        Frame.Width + ToolbarLayout.HitPadding * 2,
        Frame.Height + ToolbarLayout.HitPadding * 2);
}

public sealed record ToolbarLayoutResult(
    double X,
    double Y,
    double Width,
    double Height,
    bool IsBelowAnchor,
    IReadOnlyList<ToolbarRect> RowFrames,
    IReadOnlyList<ToolbarSlot> Slots);

/// <summary>
/// 双行工具栏的纯几何。它只认识控件宽度与分组，不依赖 WinUI。
/// </summary>
public static class ToolbarLayout
{
    public const double RowHeight = 34;
    public const double InterRowGap = 8;
    public const double AnchorGap = 8;
    public const double HitPadding = 4;
    public const double EdgeInset = 8;
    public const double HorizontalPadding = 6;
    public const double SeparatorWidth = 9;
    public const double IconButtonWidth = 30;
    public const double StyleButtonWidth = 22;

    public static ToolbarLayoutResult Arrange(
        IReadOnlyList<IToolbarControl> controls,
        ToolbarContext context,
        ToolbarRect viewport,
        ToolbarRect anchor)
    {
        var main = controls.Where(control => control.Group != ToolbarGroup.Style).ToArray();
        var style = controls.Where(control => control.Group == ToolbarGroup.Style).ToArray();

        double mainWidth = RowWidth(main, context);
        double styleWidth = RowWidth(style, context);
        double width = Math.Max(mainWidth, styleWidth);
        bool hasStyle = style.Length > 0;
        double height = hasStyle ? RowHeight * 2 + InterRowGap : RowHeight;
        bool below = anchor.Bottom + AnchorGap + height <= viewport.Bottom - EdgeInset;

        double x = Math.Clamp(
            anchor.X + (anchor.Width - width) / 2,
            viewport.X + EdgeInset,
            Math.Max(viewport.X + EdgeInset, viewport.Right - EdgeInset - width));
        double y = below
            ? anchor.Bottom + AnchorGap
            : Math.Max(viewport.Y + EdgeInset, anchor.Y - AnchorGap - height);

        double mainY = below || !hasStyle ? 0 : RowHeight + InterRowGap;
        double styleY = below ? RowHeight + InterRowGap : 0;
        var rows = new List<ToolbarRect>();
        var slots = new List<ToolbarSlot>();

        if (main.Length > 0)
        {
            rows.Add(new ToolbarRect(0, mainY, mainWidth, RowHeight));
            AddRowSlots(main, context, mainY, slots);
        }
        if (hasStyle)
        {
            rows.Add(new ToolbarRect(0, styleY, styleWidth, RowHeight));
            AddRowSlots(style, context, styleY, slots);
        }

        return new ToolbarLayoutResult(x, y, width, height, below, rows, slots);
    }

    private static double RowWidth(IReadOnlyList<IToolbarControl> controls, ToolbarContext context)
    {
        if (controls.Count == 0) return 0;
        double width = HorizontalPadding * 2;
        ToolbarGroup? previous = null;
        foreach (var control in controls)
        {
            if (previous.HasValue && previous.Value != control.Group)
                width += SeparatorWidth;
            width += control.PreferredWidth(context);
            previous = control.Group;
        }
        return width;
    }

    private static void AddRowSlots(
        IReadOnlyList<IToolbarControl> controls,
        ToolbarContext context,
        double y,
        ICollection<ToolbarSlot> slots)
    {
        double x = HorizontalPadding;
        ToolbarGroup? previous = null;
        foreach (var control in controls)
        {
            if (previous.HasValue && previous.Value != control.Group)
            {
                slots.Add(new ToolbarSlot(new ToolbarRect(x, y, SeparatorWidth, RowHeight), null));
                x += SeparatorWidth;
            }

            double width = control.PreferredWidth(context);
            slots.Add(new ToolbarSlot(new ToolbarRect(x, y, width, RowHeight), control));
            x += width;
            previous = control.Group;
        }
    }
}
