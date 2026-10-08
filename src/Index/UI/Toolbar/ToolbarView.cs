using Index.Toolbar;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Toolbar;

/// <summary>
/// Coordinates the platform-neutral toolbar registry/layout with the WinUI renderer.
/// </summary>
public sealed class ToolbarView : Canvas
{
    private readonly ToolbarVisualStyle _visualStyle;
    private readonly ToolbarControlPresenterRegistry _presenters;
    private readonly ToolbarRenderer _renderer;
    private string _renderSignature = "";

    public ToolbarView()
        : this(ToolbarVisualStyle.Standard)
    {
    }

    public ToolbarView(ToolbarVisualStyle visualStyle)
        : this(
            visualStyle,
            ToolbarControlPresenterRegistry.CreateDefault(),
            ToolbarActivationRouter.CreateDefault())
    {
    }

    public ToolbarView(
        ToolbarVisualStyle visualStyle,
        ToolbarControlPresenterRegistry presenters,
        ToolbarActivationRouter activationRouter)
    {
        ArgumentNullException.ThrowIfNull(presenters);
        ArgumentNullException.ThrowIfNull(activationRouter);

        _visualStyle = visualStyle;
        _presenters = presenters;
        _renderer = new ToolbarRenderer(this, presenters, activationRouter);
    }

    public ToolbarLayoutResult Update(
        ToolbarRegistry registry,
        ToolbarContext context,
        ToolbarRect viewport,
        ToolbarRect anchor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context);

        var controls = registry.ControlsFor(context);
        var layout = ToolbarLayout.Arrange(controls, context, viewport, anchor);
        string signature = ToolbarRenderSignature.Create(
            controls,
            context,
            ActualTheme,
            _visualStyle,
            layout,
            _presenters);

        if (_renderSignature != signature)
        {
            _renderer.Render(layout, context, ToolbarAppearance.Resolve(ActualTheme, _visualStyle));
            _renderSignature = signature;
        }

        Width = layout.Width;
        Height = layout.Height;
        return layout;
    }
}
