using Index.App;
using Index.Settings;
using Index.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Index.UI.Gallery;
using Index.UI.Settings;
using Index.Platform.Windowing;

namespace Index.UI;

/// <summary>图库主界面 Demo：只验证两级 Tab 与页面切换。</summary>
public sealed class MainWindow : Window
{
    private readonly CaptureCoordinator _coordinator;
    private readonly ShotStore _shotStore;
    private readonly LibraryOrganizationStore _libraryOrganization;
    private readonly IShortcutSettingsStore _shortcutSettings;
    private readonly ClipboardPopupModule _clipboardPopup;
    private readonly Grid _content = new();
    private readonly StackPanel _subTabs = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly GalleryTheme _theme = new();
    private readonly Dictionary<string, Button> _topButtons = new();
    private readonly Dictionary<string, Button> _subButtons = new();
    private ShotPreviewWindow? _previewWindow;
    private ShotGalleryGridView? _previewGallery;
    private int _contentGeneration;
    private string _activeTopTab = "library";
    private string _activeLibrarySection = "shots";

    public MainWindow(
        CaptureCoordinator coordinator,
        ShotStore shotStore,
        LibraryOrganizationStore libraryOrganization,
        IShortcutSettingsStore shortcutSettings,
        ClipboardPopupModule clipboardPopup)
    {
        _coordinator = coordinator;
        _shotStore = shotStore;
        _libraryOrganization = libraryOrganization;
        _shortcutSettings = shortcutSettings;
        _clipboardPopup = clipboardPopup;
        _shotStore.CaptureSaved += OnCaptureSaved;
        Closed += OnClosed;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Index.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);
        AppWindow.Title = "Index";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));

        var root = new Grid
        {
            Background = _theme.WindowBackground,
            Padding = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 16)
        };
        AddTopTab(top, "library", "内容库");
        AddTopTab(top, "collections", "收藏");
        AddTopTab(top, "apps", "应用");
        AddTopTab(top, "settings", "设置");
        root.Children.Add(top);

        var panel = new Border
        {
            Background = _theme.Panel,
            BorderBrush = _theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            Child = _content
        };
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Content = root;
        root.ActualThemeChanged += (_, _) => ApplyTheme(root.ActualTheme);
        ApplyTheme(root.ActualTheme);
        ShowLibrary();
    }

    private void AddTopTab(Panel host, string id, string title)
    {
        var button = MakeButton(title);
        button.Click += (_, _) =>
        {
            _activeTopTab = id;
            Select(_topButtons, id);
            switch (id)
            {
                case "library":
                    ShowLibrary();
                    break;
                case "settings":
                    ShowShortcutSettings();
                    break;
                case "collections":
                    ShowCollections();
                    break;
                default:
                    ShowSimplePage(title, "按来源应用浏览截图");
                    break;
            }
        };
        _topButtons[id] = button;
        host.Children.Add(button);
    }

    private void ShowLibrary()
    {
        _activeTopTab = "library";
        Select(_topButtons, "library");
        _content.Children.Clear();
        _content.RowDefinitions.Clear();
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _subTabs.Children.Clear();
        _subButtons.Clear();
        AddSubTab("shots", "截图");
        AddSubTab("recordings", "录屏");
        AddSubTab("clipboard", "剪切板");
        AddSubTab("ai", "AI 对话");
        _content.Children.Add(_subTabs);
        ShowLibrarySection("shots", "截图", "截图卡片将在下一步接入");
    }

    private void AddSubTab(string id, string title)
    {
        var button = MakeButton(title, compact: true);
        button.Click += (_, _) =>
        {
            var description = id switch
            {
                "recordings" => "录屏内容会显示在这里",
                "clipboard" => "剪切板历史会显示在这里",
                "ai" => "AI 对话历史会显示在这里",
                _ => "截图卡片将在下一步接入"
            };
            ShowLibrarySection(id, title, description);
        };
        _subButtons[id] = button;
        _subTabs.Children.Add(button);
    }

    private void ShowLibrarySection(string id, string title, string description)
    {
        _activeLibrarySection = id;
        Select(_subButtons, id);
        var generation = ++_contentGeneration;
        if (_content.Children.Count > 1)
            _content.Children.RemoveAt(1);

        if (id == "shots")
        {
            var loading = MakeCenteredMessage("正在读取图库…", "");
            Grid.SetRow(loading, 1);
            _content.Children.Add(loading);
            _ = LoadShotGalleryAsync(generation);
            return;
        }

        if (id == "clipboard")
        {
            var clipboard = MakeCenteredMessage(
                "剪切板弹窗",
                "持续记录文本、图片和文件；相同内容会自动去重");
            var open = MakeButton("打开剪切板弹窗");
            open.Margin = new Thickness(0, 8, 0, 0);
            open.Click += (_, _) => _clipboardPopup.Toggle();
            clipboard.Children.Add(open);
            Grid.SetRow(clipboard, 1);
            _content.Children.Add(clipboard);
            return;
        }

        var body = MakeCenteredMessage(title, description);
        Grid.SetRow(body, 1);
        _content.Children.Add(body);
    }

    private void OnCaptureSaved(StoredCapture capture)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_activeTopTab != "library" || _activeLibrarySection != "shots")
                return;
            var generation = ++_contentGeneration;
            _ = LoadShotGalleryAsync(generation);
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _shotStore.CaptureSaved -= OnCaptureSaved;
        Closed -= OnClosed;
    }

    private async Task LoadShotGalleryAsync(int generation)
    {
        try
        {
            var countTask = _shotStore.GetCountAsync();
            var firstPageTask = _shotStore.GetPageAsync();
            await Task.WhenAll(countTask, firstPageTask);
            var totalCount = await countTask;
            var firstPage = await firstPageTask;
            var shots = firstPage.Items;
            if (generation != _contentGeneration)
                return;

            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(0, 16, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = $"所有截图  ·  {totalCount:N0} 个项目",
                FontSize = 21,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = _theme.Text,
                VerticalAlignment = VerticalAlignment.Center
            });
            var refresh = MakeButton("刷新", compact: true);
            refresh.Click += (_, _) =>
            {
                var nextGeneration = ++_contentGeneration;
                _ = LoadShotGalleryAsync(nextGeneration);
            };
            Grid.SetColumn(refresh, 1);
            header.Children.Add(refresh);
            page.Children.Add(header);

            FrameworkElement body;
            if (shots.Count == 0)
            {
                var empty = MakeCenteredMessage("还没有截图", "按 Ctrl + Shift + A 开始截图");
                var capture = MakeButton("开始截图");
                capture.Margin = new Thickness(0, 8, 0, 0);
                capture.Click += async (_, _) => await _coordinator.BeginCaptureAsync("gallery");
                empty.Children.Add(capture);
                body = empty;
            }
            else
            {
                var gallery = new ShotGalleryGridView(firstPage, _shotStore, _theme);
                var detail = new ShotDetailPane(
                    _shotStore,
                    new GalleryShotCommands(_shotStore, _libraryOrganization, DispatcherQueue),
                    _theme);
                gallery.SelectionChanged += detail.ShowShot;
                gallery.PreviewRequested += shot => OpenPreview(gallery, shot);
                detail.PreviewRequested += shot => OpenPreview(gallery, shot);
                detail.ShotDeleted += deletedShot =>
                {
                    var nextGeneration = ++_contentGeneration;
                    _ = LoadShotGalleryAsync(nextGeneration);
                };
                var split = new Grid { ColumnSpacing = 16 };
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                split.Children.Add(gallery);
                Grid.SetColumn(detail, 1);
                split.Children.Add(detail);
                body = split;
            }
            Grid.SetRow(body, 1);
            page.Children.Add(body);

            if (_content.Children.Count > 1)
                _content.Children.RemoveAt(1);
            Grid.SetRow(page, 1);
            _content.Children.Add(page);
        }
        catch (Exception error)
        {
            if (generation != _contentGeneration)
                return;
            if (_content.Children.Count > 1)
                _content.Children.RemoveAt(1);
            var failed = MakeCenteredMessage("图库读取失败", error.Message);
            Grid.SetRow(failed, 1);
            _content.Children.Add(failed);
        }
    }

    private void ShowCollections()
    {
        Select(_topButtons, "collections");
        _content.Children.Clear();
        _content.RowDefinitions.Clear();
        var generation = ++_contentGeneration;
        _content.Children.Add(MakeCenteredMessage("正在读取收藏…", ""));
        _ = LoadCollectionsAsync(generation);
    }

    private void OpenPreview(ShotGalleryGridView gallery, ShotRecord shot)
    {
        var created = false;
        if (!ReferenceEquals(_previewGallery, gallery))
        {
            if (_previewGallery is not null)
                _previewGallery.SelectionChanged -= SyncPreviewSelection;
            _previewGallery = gallery;
            _previewGallery.SelectionChanged += SyncPreviewSelection;
        }

        if (_previewWindow is null)
        {
            _previewWindow = new ShotPreviewWindow(_shotStore, _theme, shot);
            _previewWindow.NavigationRequested += NavigatePreview;
            _previewWindow.Closed += OnPreviewClosed;
            created = true;
        }
        else
        {
            _previewWindow.ShowShot(shot);
        }

        _previewWindow.Activate();
        if (created)
        {
            OwnedWindowRelationship.Attach(this, _previewWindow);
            _previewWindow.Activate();
        }
    }

    private void SyncPreviewSelection(ShotRecord shot)
        => _previewWindow?.ShowShot(shot);

    private async void NavigatePreview(int delta)
    {
        if (_previewGallery is not null)
            await _previewGallery.MoveSelectionAsync(delta);
    }

    private void OnPreviewClosed(object sender, WindowEventArgs args)
    {
        if (_previewWindow is not null)
        {
            _previewWindow.NavigationRequested -= NavigatePreview;
            _previewWindow.Closed -= OnPreviewClosed;
            _previewWindow = null;
        }
        if (_previewGallery is not null)
        {
            _previewGallery.SelectionChanged -= SyncPreviewSelection;
            _previewGallery = null;
        }
    }

    private async Task LoadCollectionsAsync(int generation)
    {
        try
        {
            var favorites = await _libraryOrganization.GetFavoriteIdsAsync();
            var collections = await _libraryOrganization.GetCollectionsAsync();
            if (generation != _contentGeneration) return;

            var root = new StackPanel { Spacing = 18 };
            root.Children.Add(new TextBlock
            {
                Text = "收藏",
                FontSize = 26,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = _theme.Text
            });

            var cards = new StackPanel { Spacing = 10 };
            cards.Children.Add(CollectionRow("快速收藏", $"{favorites.Count} 张截图", "\uE734"));
            foreach (var collection in collections)
            {
                var subtitle = $"{collection.ItemCount} 张截图";
                if (!string.IsNullOrWhiteSpace(collection.Note))
                    subtitle += $" · {collection.Note}";
                cards.Children.Add(CollectionRow(collection.Name, subtitle, "\uE8B7"));
            }
            if (collections.Count == 0)
            {
                cards.Children.Add(new TextBlock
                {
                    Text = "还没有专题集。数据库已经准备好，下一步接入创建和成员管理。",
                    FontSize = 13,
                    Foreground = _theme.Muted,
                    Margin = new Thickness(4, 8, 4, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            root.Children.Add(cards);

            _content.Children.Clear();
            _content.Children.Add(new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
        }
        catch (Exception error)
        {
            if (generation != _contentGeneration) return;
            _content.Children.Clear();
            _content.Children.Add(MakeCenteredMessage("收藏读取失败", error.Message));
        }
    }

    private Border CollectionRow(string title, string subtitle, string glyph)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = _theme.Selected,
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 18,
                Foreground = _theme.Text
            }
        });
        var labels = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = _theme.Text
                },
                new TextBlock { Text = subtitle, FontSize = 12, Foreground = _theme.Muted }
            }
        };
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        return new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = row
        };
    }

    private StackPanel MakeCenteredMessage(string title, string description) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = title, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = _theme.Text, HorizontalAlignment = HorizontalAlignment.Center },
            new TextBlock { Text = description, FontSize = 14, Foreground = _theme.Muted, HorizontalAlignment = HorizontalAlignment.Center }
        }
    };

    private void ShowSimplePage(string title, string description)
    {
        _contentGeneration++;
        _content.Children.Clear();
        _content.RowDefinitions.Clear();
        _content.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = _theme.Text, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = description, FontSize = 14, Foreground = _theme.Muted, HorizontalAlignment = HorizontalAlignment.Center }
            }
        });
    }

    private void ShowShortcutSettings()
    {
        _contentGeneration++;
        _content.Children.Clear();
        _content.RowDefinitions.Clear();
        _content.Children.Add(new ShortcutSettingsView(_shortcutSettings)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        });
    }

    public void ShowLibraryPage()
    {
        Activate();
        ShowLibrary();
    }

    private Button MakeButton(string title, bool compact = false) => new()
    {
        Content = title,
        Padding = new Thickness(compact ? 14 : 20, compact ? 7 : 9, compact ? 14 : 20, compact ? 7 : 9),
        CornerRadius = new CornerRadius(18),
        Background = new SolidColorBrush(Colors.Transparent),
        Foreground = _theme.Muted,
        BorderThickness = new Thickness(0)
    };

    private void Select(Dictionary<string, Button> buttons, string selected)
    {
        foreach (var pair in buttons)
        {
            pair.Value.Background = pair.Key == selected ? _theme.Selected : new SolidColorBrush(Colors.Transparent);
            pair.Value.Foreground = pair.Key == selected ? _theme.Text : _theme.Muted;
        }
    }

    private void ApplyTheme(ElementTheme theme)
        => _theme.Apply(theme);
}
