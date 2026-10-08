using Index.Annotation;
using Index.Toolbar;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Toolbar;

public interface IToolbarActivationHandler
{
    bool CanHandle(IToolbarControl control);
    void Activate(Button anchor, IToolbarControl control, ToolbarContext context);
}

public sealed class ToolbarActivationRouter
{
    private readonly List<IToolbarActivationHandler> _handlers = [];

    public void Register(IToolbarActivationHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(handler);
    }

    internal void Activate(
        Button anchor,
        IToolbarControl control,
        ToolbarContext context)
    {
        var handler = _handlers.FirstOrDefault(candidate => candidate.CanHandle(control));
        if (handler is null)
            control.Activate(context);
        else
            handler.Activate(anchor, control, context);
    }

    public static ToolbarActivationRouter CreateDefault()
    {
        var router = new ToolbarActivationRouter();
        router.Register(new MoreToolsActivationHandler());
        router.Register(new ShapeActivationHandler());
        return router;
    }
}

internal sealed class MoreToolsActivationHandler : IToolbarActivationHandler
{
    public bool CanHandle(IToolbarControl control)
        => control is MoreAnnotationToolsToolbarControl;

    public void Activate(
        Button anchor,
        IToolbarControl control,
        ToolbarContext context)
    {
        var moreTools = (MoreAnnotationToolsToolbarControl)control;
        var menu = new MenuFlyout();
        foreach (var descriptor in moreTools.Descriptors)
        {
            var tool = descriptor.Tool;
            var item = new MenuFlyoutItem
            {
                Text = tool.Title(),
                Icon = ToolbarIconCatalog.Create(
                    AnnotationToolbarControlIds.Tool(tool),
                    "\u2022",
                    anchor.Foreground,
                    16)
            };
            item.Click += (_, _) => moreTools.SelectTool(tool, context);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }
}

internal sealed class ShapeActivationHandler : IToolbarActivationHandler
{
    public bool CanHandle(IToolbarControl control)
        => control is ShapeAnnotationToolbarControl;

    public void Activate(
        Button anchor,
        IToolbarControl control,
        ToolbarContext context)
    {
        var shape = (ShapeAnnotationToolbarControl)control;
        var menu = new MenuFlyout();
        foreach (var tool in ShapeAnnotationToolbarControl.Tools)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = tool.Title(),
                IsChecked = context.Annotation.Tool == tool,
                Icon = ToolbarIconCatalog.Create(
                    AnnotationToolbarControlIds.Tool(tool),
                    "\u25A1",
                    anchor.Foreground,
                    16)
            };
            item.Click += (_, _) => shape.SelectTool(tool, context);
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }
}
