using Index.Toolbar;
using Microsoft.UI.Xaml;

namespace Index.UI.Toolbar;

internal static class ToolbarRenderSignature
{
    public static string Create(
        IReadOnlyList<IToolbarControl> controls,
        ToolbarContext context,
        ElementTheme theme,
        ToolbarVisualStyle visualStyle,
        ToolbarLayoutResult layout,
        ToolbarControlPresenterRegistry presenters)
    {
        string controlsSignature = string.Join('|', controls.Select(control =>
            $"{control.Id}:{control.IsEnabled(context)}:{control.IsSelected(context)}:" +
            $"{control.IsBusy(context)}:{presenters.StateSignature(control, context)}"));
        return $"{theme}:{visualStyle}:{layout.IsBelowAnchor}:{controlsSignature}";
    }
}
