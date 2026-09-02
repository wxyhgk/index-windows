using Index.App;
using Index.Clipboard;
using Index.Capture;
using Index.Navigation;
using Index.Platform;
using Index.Platform.Clipboard;
using Index.Recognition;
using Index.Search;
using Index.Settings;
using Index.Storage;
using Index.UI.Gallery;
using Index.UI.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Index.UI;

/// <summary>图库主界面 Demo：只验证两级 Tab 与页面切换。</summary>
public sealed partial class MainWindow : Window
{
    private readonly CaptureCoordinator _coordinator;
    private readonly ShotStore _shotStore;
    private readonly LibraryOrganizationStore _libraryOrganization;
    private readonly IShortcutSettingsStore _shortcutSettings;
    private readonly VirtualDisplayCaptureWorkflow _virtualDisplayCapture;
    private readonly IClipboardHistorySource _clipboardHistory;
    private readonly IClipboardWriter _clipboardWriter;
    private readonly IUnifiedSearchService _unifiedSearch;
    private readonly IShotAssetReader _shotAssets;
    private readonly IRecognitionPluginRegistry _recognitionPlugins;
    private readonly MainContentHost _contentHost = new();
    private readonly StackPanel _subTabs = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly GalleryTheme _theme = new();
    private readonly MainNavigationState _navigation = new();
    private readonly Dictionary<MainNavigationPage, Button> _topButtons = new();
    private readonly Dictionary<LibrarySection, Button> _subButtons = new();
    private ShotPreviewView? _previewView;
    private ShotGalleryGridView? _previewGallery;
    private ShotGalleryGridView? _activeGallery;
    private UnifiedSearchView? _searchView;
    private bool _galleryRefreshPending;
    private bool _closeToTrayEnabled;
    private bool _exitRequested;
    private CancellationTokenSource? _trayTrimCancellation;

    public MainWindow(
        CaptureCoordinator coordinator,
        ShotStore shotStore,
        LibraryOrganizationStore libraryOrganization,
        IShortcutSettingsStore shortcutSettings,
        VirtualDisplayCaptureWorkflow virtualDisplayCapture,
        IClipboardHistorySource clipboardHistory,
        IClipboardWriter clipboardWriter,
        IShotAssetReader shotAssets,
        IRecognitionPluginRegistry recognitionPlugins)
    {
        _coordinator = coordinator;
        _shotStore = shotStore;
        _libraryOrganization = libraryOrganization;
        _shortcutSettings = shortcutSettings;
        _virtualDisplayCapture = virtualDisplayCapture
            ?? throw new ArgumentNullException(nameof(virtualDisplayCapture));
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
        AddTopTab(top, MainNavigationPage.Library, "内容库");
        AddTopTab(top, MainNavigationPage.Collections, "收藏");
        AddTopTab(top, MainNavigationPage.Apps, "应用");
        AddTopTab(top, MainNavigationPage.Settings, "设置");
        root.Children.Add(top);

        var panel = new Border
        {
            Background = _theme.Panel,
            BorderBrush = _theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            Child = _contentHost
        };
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        Content = root;
        root.ActualThemeChanged += (_, _) => ApplyTheme(root.ActualTheme);
        ApplyTheme(root.ActualTheme);
        ShowLibrary();
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
