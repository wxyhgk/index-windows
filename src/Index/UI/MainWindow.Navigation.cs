using Index.Navigation;
using Index.UI.Applications;
using Index.UI.Clipboard;
using Index.UI.Search;
using Index.UI.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI;

public sealed partial class MainWindow
{
    private void AddTopTab(Panel host, MainNavigationPage page, string title)
    {
        var button = MakeButton(title);
        button.Click += (_, _) =>
        {
            Select(_topButtons, page);
            switch (page)
            {
                case MainNavigationPage.Library:
                    ShowLibrary();
                    break;
                case MainNavigationPage.Settings:
                    ShowShortcutSettings();
                    break;
                case MainNavigationPage.Collections:
                    ShowCollections();
                    break;
                case MainNavigationPage.Apps:
                    ShowApplications();
                    break;
                default:
                    ShowSimplePage(page, title, "按来源应用浏览截图");
                    break;
            }
        };
        _topButtons[page] = button;
        host.Children.Add(button);
    }

    private void ShowLibrary()
    {
        PrepareForNavigation();
        Select(_topButtons, MainNavigationPage.Library);

        _subTabs.Children.Clear();
        _subButtons.Clear();
        AddSubTab(LibrarySection.Shots, "截图");
        AddSubTab(LibrarySection.Recordings, "录屏");
        AddSubTab(LibrarySection.Clipboard, "剪切板");
        AddSubTab(LibrarySection.Ai, "AI 对话");
        _contentHost.ShowLibrary(_subTabs);
        ShowLibrarySection(LibrarySection.Shots, "截图", "截图卡片将在下一步接入");
    }

    private void AddSubTab(LibrarySection section, string title)
    {
        var button = MakeButton(title, compact: true);
        button.Click += (_, _) =>
        {
            var description = section switch
            {
                LibrarySection.Recordings => "录屏内容会显示在这里",
                LibrarySection.Clipboard => "剪切板历史会显示在这里",
                LibrarySection.Ai => "AI 对话历史会显示在这里",
                _ => "截图卡片将在下一步接入"
            };
            ShowLibrarySection(section, title, description);
        };
        _subButtons[section] = button;
        _subTabs.Children.Add(button);
    }

    private void ShowLibrarySection(
        LibrarySection section,
        string title,
        string description)
    {
        DisposeActiveGallery();
        Select(_subButtons, section);
        var generation = _navigation.ShowLibrary(section);

        if (section == LibrarySection.Shots)
        {
            var loading = MakeCenteredMessage("正在读取图库…", "");
            _contentHost.ShowLibraryBody(loading);
            _ = LoadShotGalleryAsync(generation);
            return;
        }

        if (section == LibrarySection.Clipboard)
        {
            var clipboard = new ClipboardLibraryView(
                _clipboardHistory,
                _clipboardWriter,
                _theme);
            _contentHost.ShowLibraryBody(clipboard);
            return;
        }

        var body = MakeCenteredMessage(title, description);
        _contentHost.ShowLibraryBody(body);
    }

    private void ShowUnifiedSearch()
    {
        PrepareForNavigation();
        _navigation.ShowPage(MainNavigationPage.Search);
        Select(_topButtons, MainNavigationPage.Search);
        _searchView = new UnifiedSearchView(
            _unifiedSearch,
            _shotAssets,
            _clipboardWriter,
            _theme);
        _contentHost.ShowPage(_searchView);
    }

    private void CloseSearch()
    {
        _searchView?.Dispose();
        _searchView = null;
    }

    private void ShowSimplePage(
        MainNavigationPage page,
        string title,
        string description)
    {
        PrepareForNavigation();
        _navigation.ShowPage(page);
        _contentHost.ShowPage(new StackPanel
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
        PrepareForNavigation();
        _navigation.ShowPage(MainNavigationPage.Settings);
        _contentHost.ShowPage(new ShortcutSettingsView(
            _shortcutSettings,
            _virtualDisplayCapture)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        });
    }

    private void ShowApplications()
    {
        PrepareForNavigation();
        Select(_topButtons, MainNavigationPage.Apps);
        _navigation.ShowPage(MainNavigationPage.Apps);
        _applicationsView = new ApplicationsWorkspaceView(
            _shotStore,
            _libraryOrganization,
            _shotAssets,
            _theme);
        _applicationsView.PreviewRequested += OpenPreview;
        _contentHost.ShowPage(_applicationsView);
        _applicationsView.Start();
    }

    public void ShowLibraryPage()
    {
        ShowFromTray();
        ShowLibrary();
    }

    private void PrepareForNavigation()
    {
        ClosePreview();
        CloseSearch();
        DisposeActiveGallery();
        DisposeApplicationsWorkspace();
    }

    private void DisposeApplicationsWorkspace()
    {
        if (_applicationsView is null)
            return;
        _applicationsView.PreviewRequested -= OpenPreview;
        _applicationsView.Dispose();
        _applicationsView = null;
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

    private void Select<TKey>(
        IReadOnlyDictionary<TKey, Button> buttons,
        TKey selected)
        where TKey : notnull
    {
        foreach (var pair in buttons)
        {
            var isSelected = EqualityComparer<TKey>.Default.Equals(pair.Key, selected);
            pair.Value.Background = isSelected
                ? _theme.Selected
                : new SolidColorBrush(Colors.Transparent);
            pair.Value.Foreground = isSelected ? _theme.Text : _theme.Muted;
        }
    }

}
