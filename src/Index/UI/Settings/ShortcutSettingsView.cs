using Index.Settings;
using Index.Capture;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Settings;

/// <summary>Embeddable settings panel for the Windows global shortcuts.</summary>
public sealed class ShortcutSettingsView : UserControl, IDisposable
{
    private readonly IShortcutSettingsStore _store;
    private readonly VirtualDisplayCaptureWorkflow _virtualDisplayCapture;
    private readonly DispatcherQueue _dispatcher;
    private readonly ShortcutRecorderControl _captureRecorder;
    private readonly ShortcutRecorderControl _virtualWindowRecorder;
    private readonly ShortcutRecorderControl _galleryRecorder;
    private readonly ShortcutRecorderControl _clipboardRecorder;
    private readonly TextBlock _status;
    private readonly TextBlock _virtualDisplayStatus;
    private readonly Button _virtualCaptureButton;
    private readonly ToggleSwitch _automatic4KCapture;
    private CancellationTokenSource? _virtualCaptureCancellation;
    private bool _refreshingControls;
    private bool _disposed;

    public ShortcutSettingsView(
        IShortcutSettingsStore store,
        VirtualDisplayCaptureWorkflow virtualDisplayCapture)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _virtualDisplayCapture = virtualDisplayCapture
            ?? throw new ArgumentNullException(nameof(virtualDisplayCapture));
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "Shortcut settings must be created on the UI thread.");
        _captureRecorder = new ShortcutRecorderControl(store.Current.Capture);
        _virtualWindowRecorder = new ShortcutRecorderControl(store.Current.VirtualWindow);
        _galleryRecorder = new ShortcutRecorderControl(store.Current.Gallery);
        _clipboardRecorder = new ShortcutRecorderControl(store.Current.Clipboard);
        _status = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };
        _virtualDisplayStatus = new TextBlock
        {
            FontSize = 13,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };
        _virtualCaptureButton = new Button
        {
            Content = "截取完整虚拟屏",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _virtualCaptureButton.Click += OnVirtualCaptureClicked;
        _automatic4KCapture = new ToggleSwitch
        {
            Header = "自动 4K 窗口截图",
            OffContent = "关闭",
            OnContent = "开启",
            IsOn = store.Current.Automatic4KCapture
        };
        _automatic4KCapture.Toggled += (_, _) =>
        {
            if (!_refreshingControls)
                Save(automatic4KCapture: _automatic4KCapture.IsOn);
        };

        _captureRecorder.ShortcutRecorded += (_, shortcut) => Save(capture: shortcut);
        _virtualWindowRecorder.ShortcutRecorded += (_, shortcut) => Save(virtualWindow: shortcut);
        _galleryRecorder.ShortcutRecorded += (_, shortcut) => Save(gallery: shortcut);
        _clipboardRecorder.ShortcutRecorded += (_, shortcut) => Save(clipboard: shortcut);

        var reset = new Button
        {
            Content = "恢复默认",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        reset.Click += (_, _) =>
        {
            _store.Reset();
            Refresh();
            _status.Text = "已恢复默认快捷键。";
        };

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = "全局快捷键",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "点击组合键后，直接按下新的快捷键。Esc 可取消录制。",
            FontSize = 13,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(MakeRow("截图", "冻结屏幕并开始框选", _captureRecorder));
        content.Children.Add(MakeRow(
            "4K 当前窗口",
            "保持逻辑大小，在 4K 虚拟屏上按高 DPI 渲染并保存",
            _virtualWindowRecorder));
        content.Children.Add(MakeRow("打开图库", "显示 Index 内容库", _galleryRecorder));
        content.Children.Add(MakeRow("剪贴板弹窗", "显示或隐藏剪贴板历史", _clipboardRecorder));
        content.Children.Add(_status);
        content.Children.Add(reset);
        content.Children.Add(new Border
        {
            Height = 1,
            Opacity = 0.18,
            Margin = new Thickness(0, 8, 0, 8)
        });
        content.Children.Add(new TextBlock
        {
            Text = "高分辨率虚拟屏截图",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "自动模式开启后，普通框选的完成或复制会自动使用 4K 高 DPI 窗口帧；无法关联到单个窗口时回退普通截图。",
            FontSize = 13,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(_automatic4KCapture);
        content.Children.Add(_virtualDisplayStatus);
        content.Children.Add(_virtualCaptureButton);
        var openDisplaySettings = new Button
        {
            Content = "打开 Windows 显示设置",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        openDisplaySettings.Click += (_, _) => OpenDisplaySettings();
        content.Children.Add(openDisplaySettings);

        var settingsCard = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Child = content,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Content = new ScrollViewer
        {
            Content = settingsCard,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
        => RefreshVirtualDisplayStatus();

    private void OnUnloaded(object sender, RoutedEventArgs args)
        => CancelVirtualCapture();

    private static Grid MakeRow(string title, string description, FrameworkElement recorder)
    {
        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        labels.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.68 });
        row.Children.Add(labels);
        Grid.SetColumn(recorder, 1);
        row.Children.Add(recorder);
        return row;
    }

    private void Save(
        KeyboardShortcut? capture = null,
        KeyboardShortcut? gallery = null,
        KeyboardShortcut? clipboard = null,
        KeyboardShortcut? virtualWindow = null,
        bool? automatic4KCapture = null)
    {
        var current = _store.Current;
        var next = new ShortcutSettings(
            capture ?? current.Capture,
            gallery ?? current.Gallery,
            clipboard ?? current.Clipboard,
            virtualWindow ?? current.VirtualWindow,
            automatic4KCapture ?? current.Automatic4KCapture);
        try
        {
            _store.Save(next);
            _status.Text = automatic4KCapture.HasValue
                ? automatic4KCapture.Value
                    ? "自动 4K 窗口截图已开启。"
                    : "自动 4K 窗口截图已关闭。"
                : "快捷键已保存并立即生效。";
        }
        catch (ArgumentException error)
        {
            Refresh();
            _status.Text = error.Message;
        }
    }

    private void Refresh()
    {
        _refreshingControls = true;
        try
        {
            _captureRecorder.SetShortcut(_store.Current.Capture);
            _virtualWindowRecorder.SetShortcut(_store.Current.VirtualWindow);
            _galleryRecorder.SetShortcut(_store.Current.Gallery);
            _clipboardRecorder.SetShortcut(_store.Current.Clipboard);
            _automatic4KCapture.IsOn = _store.Current.Automatic4KCapture;
        }
        finally
        {
            _refreshingControls = false;
        }
    }

    private void RefreshVirtualDisplayStatus()
    {
        try
        {
            var status = _virtualDisplayCapture.GetStatus();
            switch (status.Availability)
            {
                case VirtualDisplayAvailability.Active:
                    _virtualDisplayStatus.Text =
                        $"已连接：{status.DisplayName ?? "MTT 虚拟显示器"} · " +
                        $"{status.Width}×{status.Height}";
                    _virtualCaptureButton.IsEnabled = true;
                    break;
                case VirtualDisplayAvailability.InstalledInactive:
                    _virtualDisplayStatus.Text =
                        "驱动已安装。截图时会临时启用 3840×2160 虚拟屏，完成后恢复原显示布局。";
                    _virtualCaptureButton.IsEnabled = true;
                    break;
                default:
                    _virtualDisplayStatus.Text =
                        "未检测到 MTT Virtual Display Driver；普通截图不受影响。";
                    _virtualCaptureButton.IsEnabled = false;
                    break;
            }
        }
        catch (Exception error)
        {
            _virtualDisplayStatus.Text = $"读取虚拟屏状态失败：{error.Message}";
            _virtualCaptureButton.IsEnabled = false;
        }
    }

    private async void OnVirtualCaptureClicked(object sender, RoutedEventArgs args)
    {
        if (_disposed)
            return;

        CancelVirtualCapture();
        var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;
        _virtualCaptureCancellation = cancellation;
        _virtualCaptureButton.IsEnabled = false;
        _virtualDisplayStatus.Text = "正在通过 WGC 捕获虚拟屏…";
        string? finalMessage = null;
        try
        {
            var outcome = await _virtualDisplayCapture.CaptureAndSaveAsync(
                cancellationToken).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested)
                finalMessage = outcome.Message;
        }
        catch (Exception error)
        {
            if (!cancellationToken.IsCancellationRequested)
                finalMessage = $"虚拟屏截图失败：{error.Message}";
        }
        finally
        {
            bool ownsUi = ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _virtualCaptureCancellation,
                    null,
                cancellation),
                cancellation);
            cancellation.Dispose();
            if (ownsUi)
            {
                await TryRunOnUiAsync(() =>
                {
                    if (_disposed)
                        return;
                    if (finalMessage is not null)
                        _virtualDisplayStatus.Text = finalMessage;
                    try
                    {
                        _virtualCaptureButton.IsEnabled =
                            _virtualDisplayCapture.GetStatus().Availability
                            != VirtualDisplayAvailability.NotInstalled;
                    }
                    catch
                    {
                        _virtualCaptureButton.IsEnabled = false;
                    }
                }).ConfigureAwait(false);
            }
        }
    }

    private Task<bool> TryRunOnUiAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.HasThreadAccess)
        {
            try
            {
                action();
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult(true);
                }
                catch
                {
                    completion.TrySetResult(false);
                }
            }))
        {
            completion.TrySetResult(false);
        }
        return completion.Task;
    }

    private void CancelVirtualCapture()
    {
        var cancellation = Volatile.Read(ref _virtualCaptureCancellation);
        if (cancellation is null)
            return;
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void OpenDisplaySettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "ms-settings:display")
            {
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        _virtualCaptureButton.Click -= OnVirtualCaptureClicked;
        CancelVirtualCapture();
    }
}
