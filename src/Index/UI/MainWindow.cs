using Index.App;
using Index.Platform;
using Index.Settings;
using Index.Storage;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Index.UI.Gallery;
using Index.UI.Molecule;
using Index.UI.Settings;
using Index.Platform.Windowing;
using Index.Recognition;
using Index.Clipboard;
using Index.Platform.Clipboard;
using Index.UI.Clipboard;
using Index.Search;
using Index.UI.Search;

namespace Index.UI;

/// <summary>图库主界面 Demo：只验证两级 Tab 与页面切换。</summary>
public sealed class MainWindow : Window
{
    private readonly CaptureCoordinator _coordinator;
    private readonly ShotStore _shotStore;
    private readonly LibraryOrganizationStore _libraryOrganization;
    private readonly IShortcutSettingsStore _shortcutSettings;
    private readonly IClipboardHistorySource _clipboardHistory;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly IUnifiedSearchService _unifiedSearch;
    private readonly IShotAssetReader _shotAssets;
    private readonly IRecognitionPluginRegistry _recognitionPlugins;
    private readonly Grid _content = new();
    private readonly StackPanel _subTabs = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly GalleryTheme _theme = new();
    private readonly Dictionary<string, Button> _topButtons = new();
    private readonly Dictionary<string, Button> _subButtons = new();
    private ShotPreviewView? _previewView;
    private ShotGalleryGridView? _previewGallery;
    private ShotGalleryGridView? _activeGallery;
    private UnifiedSearchView? _searchView;
    private int _contentGeneration;
    private bool _galleryRefreshPending;
    private bool _closeToTrayEnabled;
    private bool _exitRequested;
    private CancellationTokenSource? _trayTrimCancellation;
    private string _activeTopTab = "library";
    private string _activeLibrarySection = "shots";

    public MainWindow(
        CaptureCoordinator coordinator,
        ShotStore shotStore,
        LibraryOrganizationStore libraryOrganization,
        IShortcutSettingsStore shortcutSettings,
        IClipboardHistorySource clipboardHistory,
        IClipboardWriter clipboardWriter,
        IShotAssetReader shotAssets,
        IRecognitionPluginRegistry recognitionPlugins)
    {
        _coordinator = coordinator;
        _shotStore = shotStore;
        _libraryOrganization = libraryOrganization;
        _shortcutSettings = shortcutSettings;
        _clipboardHistory = clipboardHistory;
        _clipboardWriter = clipboardWriter;
        _unifiedSearch = new UnifiedSearchService(_shotStore, _clipboardHistory);
        _shotAssets = shotAssets;
        _recognitionPlugins = recognitionPlugins;
        _shotStore.CaptureSaved += OnCaptureSaved;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
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
            ClosePreview();
            CloseSearch();
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
        DisposeActiveGallery();
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
        DisposeActiveGallery();
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
            var clipboard = new ClipboardLibraryView(
                _clipboardHistory,
                _clipboardWriter,
                _theme);
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
            if (_previewView is not null && _content.Children.Contains(_previewView))
            {
                _galleryRefreshPending = true;
                return;
            }
            var generation = ++_contentGeneration;
            _ = LoadShotGalleryAsync(generation);
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        CancelTrayMemoryTrim();
        ClosePreview();
        CloseSearch();
        DisposeActiveGallery();
        _previewView?.Dispose();
        _previewView = null;
        _shotStore.CaptureSaved -= OnCaptureSaved;
        AppWindow.Closing -= OnAppWindowClosing;
        Closed -= OnClosed;
    }

    private void OnAppWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (!_closeToTrayEnabled || _exitRequested)
            return;

        args.Cancel = true;
        sender.Hide();
        _activeGallery?.SetBackgrounded(true);
        ScheduleTrayMemoryTrim();
    }

    public void EnableCloseToTray() => _closeToTrayEnabled = true;

    public void ShowFromTray()
    {
        CancelTrayMemoryTrim();
        _activeGallery?.SetBackgrounded(false);
        AppWindow.Show();
        Activate();
    }

    private void ScheduleTrayMemoryTrim()
    {
        CancelTrayMemoryTrim();
        var cancellation = new CancellationTokenSource();
        _trayTrimCancellation = cancellation;
        _ = TrimTrayMemoryAsync(cancellation);
    }

    private async Task TrimTrayMemoryAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token)
                .ConfigureAwait(false);
            ThumbnailLoader.Shared.Clear();
            WebsiteFaviconProvider.Shared.Clear();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_trayTrimCancellation, cancellation))
                _trayTrimCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelTrayMemoryTrim()
    {
        var cancellation = _trayTrimCancellation;
        _trayTrimCancellation = null;
        cancellation?.Cancel();
    }

    private void ShowUnifiedSearch()
    {
        DisposeActiveGallery();
        _activeTopTab = "search";
        Select(_topButtons, "search");
        _content.Children.Clear();
        _content.RowDefinitions.Clear();
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _searchView = new UnifiedSearchView(
            _unifiedSearch,
            _shotAssets,
            _clipboardWriter,
            _theme);
        _content.Children.Add(_searchView);
    }

    private void CloseSearch()
    {
        _searchView?.Dispose();
        _searchView = null;
    }

    public void ExitApplication()
    {
        _exitRequested = true;
        Close();
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
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
            ShotGalleryGridView? nextGallery = null;
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
                nextGallery = gallery;
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

            var searchAppearance = BuildGallerySearchAppearance();
            Grid.SetRow(searchAppearance, 2);
            page.Children.Add(searchAppearance);

            if (_content.Children.Count > 1)
            {
                DisposeActiveGallery();
                _content.Children.RemoveAt(1);
            }
            Grid.SetRow(page, 1);
            _content.Children.Add(page);
            _activeGallery = nextGallery;
        }
        catch (Exception error)
        {
            if (generation != _contentGeneration)
                return;
            if (_content.Children.Count > 1)
            {
                DisposeActiveGallery();
                _content.Children.RemoveAt(1);
            }
            var failed = MakeCenteredMessage("图库读取失败", error.Message);
            Grid.SetRow(failed, 1);
            _content.Children.Add(failed);
        }
    }

    private void ShowCollections()
    {
        DisposeActiveGallery();
        Select(_topButtons, "collections");
        _content.Children.Clear();
        _content.RowDefinitions.Clear();
        var generation = ++_contentGeneration;
        _content.Children.Add(MakeCenteredMessage("正在读取收藏…", ""));
        _ = LoadCollectionsAsync(generation);
    }

    private void OpenPreview(ShotGalleryGridView gallery, ShotRecord shot)
    {
        if (!ReferenceEquals(_previewGallery, gallery))
        {
            if (_previewGallery is not null)
                _previewGallery.SelectionChanged -= SyncPreviewSelection;
            _previewGallery = gallery;
            _previewGallery.SelectionChanged += SyncPreviewSelection;
        }

        if (_previewView is null)
        {
            _previewView = new ShotPreviewView(
                _shotStore,
                _shotAssets,
                _recognitionPlugins,
                _theme,
                shot);
            _previewView.NavigationRequested += NavigatePreview;
            _previewView.CloseRequested += ClosePreview;
        }
        else
        {
            _previewView.ShowShot(shot);
        }

        if (!_content.Children.Contains(_previewView))
        {
            Grid.SetRow(_previewView, 0);
            Grid.SetRowSpan(_previewView, Math.Max(1, _content.RowDefinitions.Count));
            Canvas.SetZIndex(_previewView, 100);
            _content.Children.Add(_previewView);
        }
        _previewView.Focus(FocusState.Programmatic);
    }

    private FrameworkElement BuildGallerySearchAppearance()
    {
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 14,
            Foreground = _theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        });
        var placeholder = new TextBlock
        {
            Text = "搜索 App、标题、网址或截图里的文字",
            FontSize = 13,
            Foreground = _theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(placeholder, 1);
        content.Children.Add(placeholder);
        var shortcut = new Border
        {
            Background = _theme.Selected,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            Child = new TextBlock
            {
                Text = "Ctrl K",
                FontSize = 11,
                Foreground = _theme.Muted
            }
        };
        Grid.SetColumn(shortcut, 2);
        content.Children.Add(shortcut);
        return new Border
        {
            MaxWidth = 520,
            Height = 42,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14, 0, 10, 0),
            Background = _theme.Card,
            BorderBrush = _theme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(21),
            Child = content
        };
    }

    private void SyncPreviewSelection(ShotRecord shot)
        => _previewView?.ShowShot(shot);

    private async void NavigatePreview(int delta)
    {
        if (_previewGallery is not null)
            await _previewGallery.MoveSelectionAsync(delta);
    }

    private void ClosePreview()
    {
        var wasActive = _previewView is not null && _content.Children.Contains(_previewView);
        if (_previewView is not null)
        {
            _content.Children.Remove(_previewView);
            _previewView.Deactivate();
        }
        if (_previewGallery is not null)
        {
            _previewGallery.SelectionChanged -= SyncPreviewSelection;
            _previewGallery = null;
        }
        if (wasActive && _galleryRefreshPending
            && _activeTopTab == "library" && _activeLibrarySection == "shots")
        {
            _galleryRefreshPending = false;
            var generation = ++_contentGeneration;
            _ = LoadShotGalleryAsync(generation);
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
        DisposeActiveGallery();
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
        DisposeActiveGallery();
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
        ShowFromTray();
        ClosePreview();
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

    private void DisposeActiveGallery()
    {
        var gallery = _activeGallery;
        _activeGallery = null;
        if (ReferenceEquals(_previewGallery, gallery))
            _previewGallery = null;
        gallery?.Dispose();
    }
}
