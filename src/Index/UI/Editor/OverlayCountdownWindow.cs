using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Index.UI.Editor;

/// <summary>
/// 延迟截图倒计时浮层：显示 3→2→1 倒计时，Esc 取消。
/// 倒计时结束后触发 Completed 事件。
/// </summary>
public sealed class OverlayCountdownWindow : Window
{
    private const int CountdownSeconds = 3;

    private readonly TextBlock _countdownText;
    private readonly Grid _rootGrid;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    private int _remaining;

    /// <summary>倒计时正常结束（非取消）时触发。</summary>
    public event Action? Completed;

    public OverlayCountdownWindow()
    {
        _remaining = CountdownSeconds;

        _countdownText = new TextBlock
        {
            Text = CountdownSeconds.ToString(),
            FontSize = 72,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var titleText = new TextBlock
        {
            Text = "延迟截图",
            FontSize = 16,
            Foreground = new SolidColorBrush(Colors.White),
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var hintText = new TextBlock
        {
            Text = "Esc 取消",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var innerPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _countdownText,
                titleText,
                hintText
            }
        };

        var background = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x1E, 0x1E, 0x2E)),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(48, 36, 48, 36),
            Child = innerPanel
        };

        _rootGrid = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        _rootGrid.Children.Add(background);
        _rootGrid.KeyDown += OnKeyDown;

        Content = _rootGrid;

        Activated += (_, _) =>
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == nint.Zero) return;

            const int GwlStyle = -16;
            const int GwlExtendedStyle = -20;
            const long WsCaption = 0x00C00000L;
            const long WsThickFrame = 0x00040000L;
            const long WsExToolWindow = 0x00000080L;
            const long WsExTopmost = 0x00000008L;
            const uint SwpNoActivate = 0x0010;
            const uint SwpShowWindow = 0x0040;
            const uint SwpFrameChanged = 0x0020;

            nint style = GetWindowLongPtr(hwnd, GwlStyle);
            style = new nint(style.ToInt64() & ~(WsCaption | WsThickFrame));
            SetWindowLongPtr(hwnd, GwlStyle, style);

            nint extStyle = GetWindowLongPtr(hwnd, GwlExtendedStyle);
            extStyle = new nint(extStyle.ToInt64() | WsExToolWindow | WsExTopmost);
            SetWindowLongPtr(hwnd, GwlExtendedStyle, extStyle);

            int screenWidth = GetSystemMetrics(0);
            int screenHeight = GetSystemMetrics(1);
            int windowWidth = 280;
            int windowHeight = 220;
            int x = (screenWidth - windowWidth) / 2;
            int y = (screenHeight - windowHeight) / 2;
            SetWindowPos(hwnd, new nint(-1), x, y, windowWidth, windowHeight,
                SwpShowWindow | SwpNoActivate | SwpFrameChanged);

            _rootGrid.Focus(FocusState.Programmatic);
        };
    }

    /// <summary>显示窗口并开始倒计时。</summary>
    public void ShowAndStart()
    {
        Activate();
        Start();
    }

    public void Start()
    {
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                _timer.Stop();
                _timer = null;
                Close();
                Completed?.Invoke();
            }
            else
            {
                _countdownText.Text = _remaining.ToString();
            }
        };
        _timer.Start();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            _timer?.Stop();
            _timer = null;
            Close();
            e.Handled = true;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hwnd, nint insertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
