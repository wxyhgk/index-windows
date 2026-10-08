using Index.Annotation;
using Index.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Index.UI.Toolbar;

public interface IToolbarControlPresenter
{
    bool CanPresent(IToolbarControl control, ToolbarContext context);
    FrameworkElement Create(IToolbarControl control, ToolbarContext context, Brush foreground);
    string StateSignature(IToolbarControl control, ToolbarContext context) => "";
}

public sealed class ToolbarControlPresenterRegistry
{
    private readonly List<IToolbarControlPresenter> _presenters = [];

    public void Register(IToolbarControlPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        _presenters.Add(presenter);
    }

    internal FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
        => Resolve(control, context).Create(control, context, foreground);

    internal string StateSignature(
        IToolbarControl control,
        ToolbarContext context)
        => Resolve(control, context).StateSignature(control, context);

    public static ToolbarControlPresenterRegistry CreateDefault()
    {
        var registry = new ToolbarControlPresenterRegistry();
        registry.Register(new BusyToolbarControlPresenter());
        registry.Register(new ColorToolbarControlPresenter());
        registry.Register(new ParameterToolbarControlPresenter());
        registry.Register(new ShapeToolbarControlPresenter());
        registry.Register(new CounterToolbarControlPresenter());
        registry.Register(new HighResolutionToolbarControlPresenter());
        registry.Register(new LabeledToolbarControlPresenter());
        registry.Register(new LiveTextToolbarControlPresenter());
        registry.Register(new GlyphToolbarControlPresenter());
        return registry;
    }

    private IToolbarControlPresenter Resolve(
        IToolbarControl control,
        ToolbarContext context)
        => _presenters.FirstOrDefault(candidate => candidate.CanPresent(control, context))
            ?? throw new InvalidOperationException($"No toolbar presenter is registered for '{control.Id}'.");
}

internal sealed class BusyToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control.IsBusy(context);

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
        => new ProgressRing
        {
            Width = 14,
            Height = 14,
            IsActive = true,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
}

internal sealed class ColorToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control is AnnotationColorToolbarControl;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
    {
        var color = ((AnnotationColorToolbarControl)control).Color;
        return new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                (byte)Math.Clamp(color.A * 255, 0, 255),
                (byte)Math.Clamp(color.R * 255, 0, 255),
                (byte)Math.Clamp(color.G * 255, 0, 255),
                (byte)Math.Clamp(color.B * 255, 0, 255))),
            BorderBrush = foreground,
            BorderThickness = new Thickness(0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}

internal sealed class ParameterToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control is AnnotationStyleParameterToolbarControl;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
    {
        var parameter = (AnnotationStyleParameterToolbarControl)control;
        if (parameter.Descriptor.Axis == ToolStyleAxis.FontSize)
        {
            return new TextBlock
            {
                Text = "A",
                FontSize = 9 + parameter.Index * 2.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        double diameter = parameter.Descriptor.Axis == ToolStyleAxis.Width
            ? 3 + parameter.Index * 3
            : 10;
        double opacity = parameter.Descriptor.Axis is ToolStyleAxis.Opacity or ToolStyleAxis.Dim
            ? parameter.Value
            : 0.9;
        return new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = foreground,
            Opacity = opacity,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}

internal sealed class ShapeToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control is ShapeAnnotationToolbarControl;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
    {
        var shape = (ShapeAnnotationToolbarControl)control;
        var tool = shape.ActiveTool(context);
        return ToolbarIconCatalog.Create(
            AnnotationToolbarControlIds.Tool(tool),
            shape.Glyph,
            foreground,
            16);
    }

    public string StateSignature(IToolbarControl control, ToolbarContext context)
        => ((ShapeAnnotationToolbarControl)control).ActiveTool(context).ToString();
}

internal sealed class CounterToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control.Id == AnnotationToolbarControlIds.Tool(AnnotationTool.Counter);

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
        => ToolbarIconCatalog.CreateCounter(foreground);
}

internal sealed class HighResolutionToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control.Id == ToolbarCommandIds.HighResolution4K;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
        => new TextBlock
        {
            Text = "4K",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
}

internal sealed class LabeledToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control.ShowsLabel;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(ToolbarIconCatalog.Create(control.Id, control.Glyph, foreground, 14));
        content.Children.Add(new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        });
        return content;
    }
}

internal sealed class LiveTextToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context)
        => control.Id is ToolbarCommandIds.LiveText or ToolbarCommandIds.CopyText;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
        => ToolbarIconCatalog.CreateLiveText(foreground);
}

internal sealed class GlyphToolbarControlPresenter : IToolbarControlPresenter
{
    public bool CanPresent(IToolbarControl control, ToolbarContext context) => true;

    public FrameworkElement Create(
        IToolbarControl control,
        ToolbarContext context,
        Brush foreground)
        => ToolbarIconCatalog.Create(
            control.Id,
            control.Glyph,
            foreground,
            control.Group == ToolbarGroup.Style ? 14 : 16);
}
