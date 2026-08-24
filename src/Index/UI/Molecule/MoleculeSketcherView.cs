using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Index.UI.Molecule;

/// <summary>
/// 基于 Ketcher (WebView2) 的分子结构查看器。
/// WinUI 3 无内置 WebView2 控件，通过 CoreWebView2Controller 嵌入。
/// </summary>
public sealed class MoleculeSketcherView : UserControl, IDisposable
{
    private readonly Grid _host;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private bool _initialized;
    private bool _disposed;

    public MoleculeSketcherView()
    {
        _host = new Grid();
        Content = _host;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_controller is not null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            _controller.Bounds = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>在宿主 Window 激活后调用，传入 Window 以获取 HWND。</summary>
    public async Task InitializeAsync(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero) return;

        var environment = await CoreWebView2Environment.CreateAsync();
        var windowRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd);
        _controller = await environment.CreateCoreWebView2ControllerAsync(windowRef);
        _controller.Bounds = new Rect(0, 0, ActualWidth > 0 ? ActualWidth : 700, ActualHeight > 0 ? ActualHeight : 500);
        _webView = _controller.CoreWebView2;
        _webView.Settings.AreDefaultContextMenusEnabled = false;
        _webView.Settings.IsStatusBarEnabled = false;

        _webView.NavigateToString(BuildHtml());
        _initialized = true;
    }

    public async Task SetSdfAsync(string sdf)
    {
        if (!_initialized || _webView is not { } webView) return;
        var json = System.Text.Json.JsonSerializer.Serialize(sdf);
        await webView.ExecuteScriptAsync($"ketcher.setMolecule(JSON.parse({json}))");
    }

    public async Task SetSmilesAsync(string smiles)
    {
        if (!_initialized || _webView is not { } webView) return;
        var json = System.Text.Json.JsonSerializer.Serialize(smiles);
        await webView.ExecuteScriptAsync($"ketcher.setMolecule(JSON.parse({json}))");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SizeChanged -= OnSizeChanged;
        _controller?.Close();
        _controller = null;
        _webView = null;
    }

    private static string BuildHtml()
    {
        return """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  html, body { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: #0e1014; }
  #ketcher { width: 100%; height: 100%; }
</style>
<script src="https://cdn.jsdelivr.net/npm/ketcher@3.17.0/build/ketcher.min.js"></script>
</head>
<body>
<div id="ketcher"></div>
<script>
  const ketcher = Ketcher.create('#ketcher', {
    toolbar: true,
    menu: false,
    help: false,
    xml: {
      color: '#e0e0e0',
      bond: { color: '#e0e0e0', width: 2 },
      atom: { color: '#ffffff' }
    }
  });
  ketcher.render();
</script>
</body>
</html>
""";
    }
}
