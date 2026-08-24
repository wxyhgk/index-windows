using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.System;

namespace Index.UI.Molecule;

/// <summary>分子结构查看窗口：WebView2 + Ketcher 画布 + SMILES/置信度信息。</summary>
internal sealed class MoleculePreviewWindow : Window
{
    private readonly Grid _root;
    private readonly Border _sketcherHost;
    private readonly TextBlock _smilesLabel;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private CoreWebView2Controller? _webViewController;
    private CoreWebView2? _webView;
    private bool _closed;
    private bool _webViewInitialized;
    private string? _pendingSdf;
    private string? _pendingSmiles;

    public MoleculePreviewWindow(GalleryTheme theme, string? smiles, string? sdf, double confidence, int processingMs)
    {
        AppWindow.Title = "Index · 分子识别";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 600));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Index.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _pendingSdf = sdf;
        _pendingSmiles = smiles;

        _root = new Grid
        {
            Background = theme.WindowBackground,
            Padding = new Thickness(16),
            IsTabStop = true
        };
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "分子结构",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = theme.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        toolbar.Children.Add(title);

        var closeBtn = new Button
        {
            Content = "✕",
            Foreground = theme.Text,
            Padding = new Thickness(12, 6, 12, 6)
        };
        closeBtn.Click += (_, _) => Close();
        toolbar.Children.Add(closeBtn);
        _root.Children.Add(toolbar);

        _sketcherHost = new Border
        {
            Background = theme.ThumbnailBackground,
            BorderBrush = theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        Grid.SetRow(_sketcherHost, 1);
        _root.Children.Add(_sketcherHost);

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
            Spacing = 12
        };
        _smilesLabel = new TextBlock
        {
            Foreground = theme.Text,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Text = smiles ?? "（未识别）"
        };
        infoPanel.Children.Add(_smilesLabel);

        var infoLabel = new TextBlock
        {
            Foreground = theme.Muted,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Text = $"置信度 {confidence:P0}  ·  {processingMs}ms"
        };
        infoPanel.Children.Add(infoLabel);

        Grid.SetRow(infoPanel, 2);
        _root.Children.Add(infoPanel);

        Content = _root;

        _root.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Activated += async (_, _) =>
        {
            _root.Focus(FocusState.Programmatic);
            if (!_webViewInitialized)
            {
                _webViewInitialized = true;
                await InitializeWebViewAsync();
            }
        };
        SizeChanged += (_, _) => UpdateWebViewBounds();
        Closed += (_, _) =>
        {
            if (!_closed)
            {
                _closed = true;
                _webViewController?.Close();
                _webViewController = null;
                _webView = null;
            }
        };
    }

    /// <summary>确保回到 UI 线程（WinUI 3 的 await 不自动回到 UI 线程）。</summary>
    private Task ReturnToUiAsync()
    {
        if (_dispatcher.HasThreadAccess)
            return Task.CompletedTask;
        var tcs = new TaskCompletionSource<bool>();
        _dispatcher.TryEnqueue(() => tcs.TrySetResult(true));
        return tcs.Task;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                SetStatus("初始化失败：无法获取窗口句柄");
                return;
            }

            SetStatus("正在初始化 WebView2…");
            var environment = await CoreWebView2Environment.CreateAsync();
            await ReturnToUiAsync();

            var windowRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd);
            _webViewController = await environment.CreateCoreWebView2ControllerAsync(windowRef);
            await ReturnToUiAsync();

            _webView = _webViewController.CoreWebView2;
            _webView.Settings.AreDefaultContextMenusEnabled = false;
            _webView.Settings.IsStatusBarEnabled = false;

            // 映射本地 Ketcher 目录到虚拟主机名
            var ketcherDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Ketcher");
            if (Directory.Exists(ketcherDir))
            {
                _webView.SetVirtualHostNameToFolderMapping(
                    "ketcher.local", ketcherDir,
                    CoreWebView2HostResourceAccessKind.Allow);
            }

            // 监听 sketcherHost 尺寸变化，持续更新 WebView2 bounds
            _sketcherHost.SizeChanged += (_, _) => UpdateWebViewBounds();

            // 等布局完成（多等几帧确保 ActualWidth/Height 已更新）
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(100);
                await ReturnToUiAsync();
                if (_sketcherHost.ActualWidth > 0 && _sketcherHost.ActualHeight > 0)
                    break;
            }
            UpdateWebViewBounds();

            SetStatus("正在加载 Ketcher…");
            var navTcs = new TaskCompletionSource<bool>();
            _webView.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess)
                    navTcs.TrySetResult(true);
                else
                    navTcs.TrySetException(new Exception($"导航失败：{e.WebErrorStatus}"));
            };
            _webView.Navigate("https://ketcher.local/ketcher.html");
            await navTcs.Task;
            await ReturnToUiAsync();

            await WaitForKetcherReadyAsync();
            await SetMoleculeAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"初始化失败：{ex.Message}");
        }
    }

    private async Task WaitForKetcherReadyAsync()
    {
        if (_webView is not { } webView) return;
        for (int i = 0; i < 30; i++)
        {
            await ReturnToUiAsync();
            try
            {
                var result = await webView.ExecuteScriptAsync("typeof window.isReady === 'function' && window.isReady()");
                if (result == "true") return;
            }
            catch
            {
                // JS 还没加载完
            }
            await Task.Delay(500);
        }
    }

    private async Task SetMoleculeAsync()
    {
        if (_webView is not { } webView) return;
        try
        {
            await ReturnToUiAsync();
            UpdateWebViewBounds();

            if (!string.IsNullOrEmpty(_pendingSdf))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_pendingSdf);
                var result = await webView.ExecuteScriptAsync(
                    $"window.renderMolecule(JSON.parse({json})); 'ok'");
                SetStatus(result == "ok" ? "分子已加载" : $"加载返回：{result}");
            }
            else if (!string.IsNullOrEmpty(_pendingSmiles))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_pendingSmiles);
                var result = await webView.ExecuteScriptAsync(
                    $"window.renderMolecule(JSON.parse({json})); 'ok'");
                SetStatus(result == "ok" ? "分子已加载" : $"加载返回：{result}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"分子加载失败：{ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        if (_dispatcher.HasThreadAccess)
        {
            _smilesLabel.Text = text;
        }
        else
        {
            _dispatcher.TryEnqueue(() => _smilesLabel.Text = text);
        }
    }

    private void UpdateWebViewBounds()
    {
        if (_webViewController is null || _sketcherHost.ActualWidth <= 0 || _sketcherHost.ActualHeight <= 0)
            return;
        var transform = _sketcherHost.TransformToVisual(_root);
        var topLeft = transform.TransformPoint(new Point(0, 0));
        var newBounds = new Rect(
            topLeft.X,
            topLeft.Y,
            _sketcherHost.ActualWidth,
            _sketcherHost.ActualHeight);

        if (_webViewController.Bounds != newBounds)
        {
            _webViewController.Bounds = newBounds;
            // bounds 变化后通知 Ketcher 重新渲染（解决 canvas 高度为 0 的问题）
            if (_webView is not null)
            {
                _webView.ExecuteScriptAsync(
                    "if (typeof ketcher !== 'undefined') { ketcher.render(); }");
            }
        }
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
