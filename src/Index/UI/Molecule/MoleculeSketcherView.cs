using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Index.UI.Molecule;

/// <summary>
/// 基于 Ketcher (WebView2) 的分子结构查看器。
/// WinUI 3 无内置 WebView2 控件，通过 CoreWebView2Controller 嵌入。
/// </summary>
public sealed class MoleculeSketcherView : UserControl, IDisposable
{
    private readonly Grid _host;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private WebView2? _webViewControl;
    private CoreWebView2? _webView;
    private Task? _initializationTask;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _initialized;
    private bool _disposed;

    public MoleculeSketcherView()
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _host = new Grid();
        Content = _host;
    }

    /// <summary>在控件进入已加载的 WinUI 可视树后调用。</summary>
    public Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _initializationTask ??= InitializeCoreAsync(_lifetime.Token);
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await ReturnToUiAsync();
        cancellationToken.ThrowIfCancellationRequested();
        _webViewControl = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _host.Children.Add(_webViewControl);
        await _webViewControl.EnsureCoreWebView2Async();
        await ReturnToUiAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var webView = _webViewControl.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 核心初始化失败");
        _webView = webView;
        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;

        var ketcherDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Ketcher");
        if (!Directory.Exists(ketcherDir))
            throw new DirectoryNotFoundException($"Ketcher 资源目录不存在：{ketcherDir}");

        webView.SetVirtualHostNameToFolderMapping(
            "ketcher.local", ketcherDir,
            CoreWebView2HostResourceAccessKind.Allow);

        var navigation = new TaskCompletionSource<bool>();
        webView.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
                navigation.TrySetResult(true);
            else
                navigation.TrySetException(new InvalidOperationException($"Ketcher 导航失败：{e.WebErrorStatus}"));
        };
        webView.Navigate("https://ketcher.local/ketcher.html");
        await navigation.Task.WaitAsync(cancellationToken);

        for (var attempt = 0; attempt < 120; attempt++)
        {
            await ReturnToUiAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var ready = await webView.ExecuteScriptAsync("typeof window.isReady === 'function' && window.isReady()");
            if (ready == "true")
            {
                _initialized = true;
                return;
            }
            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Ketcher 初始化超时");
    }

    public async Task SetSdfAsync(string sdf)
    {
        if (!_initialized || _webView is not { } webView) return;
        var json = System.Text.Json.JsonSerializer.Serialize(sdf);
        await ReturnToUiAsync();
        await webView.ExecuteScriptAsync($"window.setMolecule({json})");
    }

    public async Task SetSmilesAsync(string smiles)
    {
        if (!_initialized || _webView is not { } webView) return;
        var json = System.Text.Json.JsonSerializer.Serialize(smiles);
        await ReturnToUiAsync();
        await webView.ExecuteScriptAsync($"window.setMolecule({json})");
    }

    public async Task ClearAsync()
    {
        if (!_initialized || _webView is not { } webView) return;
        await ReturnToUiAsync();
        await webView.ExecuteScriptAsync("window.setMolecule('')");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _webViewControl?.Close();
        _webViewControl = null;
        _webView = null;
        _lifetime.Dispose();
    }

    private Task ReturnToUiAsync()
    {
        if (_dispatcher.HasThreadAccess)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>();
        if (!_dispatcher.TryEnqueue(() => completion.TrySetResult(true)))
            completion.TrySetException(new InvalidOperationException("无法切换到 UI 线程"));
        return completion.Task;
    }
}
