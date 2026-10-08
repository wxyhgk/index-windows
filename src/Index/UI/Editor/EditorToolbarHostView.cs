using Index.Annotation;
using Index.Toolbar;
using Index.UI.Toolbar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Editor;

/// <summary>Editor-specific host for the shared annotation toolbar registry.</summary>
internal sealed class EditorToolbarHostView : UserControl, IDisposable
{
    private readonly ToolbarRegistry _registry = new();
    private readonly ToolbarContext _context;
    private readonly ToolbarView _toolbar = new(ToolbarVisualStyle.Standard);
    private bool _disposed;

    public EditorToolbarHostView(AnnotationState annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        AnnotationToolbarControls.RegisterEditorAnnotationDefaults(_registry);
        AnnotationStyleToolbarControls.RegisterCaptureStyleDefaults(_registry);
        _context = new ToolbarContext(annotation, ToolbarScope.Capture, _ => { });
        _toolbar.HorizontalAlignment = HorizontalAlignment.Center;
        _toolbar.VerticalAlignment = VerticalAlignment.Center;
        Content = _toolbar;
        SizeChanged += OnSizeChanged;
    }

    public void SetEditingEnabled(bool enabled)
    {
        _toolbar.IsHitTestVisible = enabled;
        _toolbar.Opacity = enabled ? 1 : 0.55;
    }

    public void Refresh()
    {
        if (_disposed)
            return;
        double width = Math.Max(420, ActualWidth);
        _toolbar.Update(
            _registry,
            _context,
            new ToolbarRect(0, 0, width, 88),
            new ToolbarRect(0, 0, width, 1));
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Refresh();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SizeChanged -= OnSizeChanged;
    }
}
