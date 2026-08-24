using Index.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI.Settings;

/// <summary>Embeddable settings panel for the two Windows global shortcuts.</summary>
public sealed class ShortcutSettingsView : UserControl
{
    private readonly IShortcutSettingsStore _store;
    private readonly ShortcutRecorderControl _captureRecorder;
    private readonly ShortcutRecorderControl _galleryRecorder;
    private readonly ShortcutRecorderControl _clipboardRecorder;
    private readonly TextBlock _status;

    public ShortcutSettingsView(IShortcutSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _captureRecorder = new ShortcutRecorderControl(store.Current.Capture);
        _galleryRecorder = new ShortcutRecorderControl(store.Current.Gallery);
        _clipboardRecorder = new ShortcutRecorderControl(store.Current.Clipboard);
        _status = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };

        _captureRecorder.ShortcutRecorded += (_, shortcut) => Save(capture: shortcut);
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
        content.Children.Add(MakeRow("打开图库", "显示 Index 内容库", _galleryRecorder));
        content.Children.Add(MakeRow("剪贴板弹窗", "显示或隐藏剪贴板历史", _clipboardRecorder));
        content.Children.Add(_status);
        content.Children.Add(reset);

        Content = new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Child = content,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

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
        KeyboardShortcut? clipboard = null)
    {
        var current = _store.Current;
        var next = new ShortcutSettings(
            capture ?? current.Capture,
            gallery ?? current.Gallery,
            clipboard ?? current.Clipboard);
        try
        {
            _store.Save(next);
            _status.Text = "快捷键已保存并立即生效。";
        }
        catch (ArgumentException error)
        {
            Refresh();
            _status.Text = error.Message;
        }
    }

    private void Refresh()
    {
        _captureRecorder.SetShortcut(_store.Current.Capture);
        _galleryRecorder.SetShortcut(_store.Current.Gallery);
        _clipboardRecorder.SetShortcut(_store.Current.Clipboard);
    }
}
