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
    private void AddTopTabs(Panel host)
    {
        var handlers = new Dictionary<MainNavigationPage, Action>
        {
            [MainNavigationPage.Library] = ShowLibrary,
            [MainNavigationPage.Collections] = ShowCollections,
            [MainNavigationPage.Apps] = ShowApplications,
            [MainNavigationPage.Settings] = ShowShortcutSettings
        };

        foreach (var destination in _topDestinations.Destinations)
        {
            if (!handlers.TryGetValue(destination.Page, out var navigate))
            {
                throw new InvalidOperationException(
                    $"Top-level destination '{destination.Id}' has no UI handler.");
            }

            AddTopTab(host, destination, navigate);
        }
    }

    private void AddTopTab(
        Panel host,
        MainNavigationDestination destination,
        Action navigate)
    {
        var button = MakeButton(destination.Title);
        button.Click += (_, _) => navigate();
        _topButtons[destination.Page] = button;
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
        _libraryWorkspaceController.CancelCurrent();
        Select(_subButtons, section);
        var generation = _navigation.ShowLibrary(section);

        if (section == LibrarySection.Shots)
        {
            var loading = MakeCenteredMessage("正在读取图库…", "");
            _pageOwner.CommitOrDispose(
                new MainPageLease(
                    new MainPageIdentity(MainNavigationPage.Library, section),
                    MainPageMount.LibraryBody,
                    loading),
                generation,
                _navigation.Generation);
            _ = LoadShotGalleryAsync(generation);
            return;
        }

        if (section == LibrarySection.Clipboard)
        {
            var clipboardView = new ClipboardLibraryView(
                _clipboardHistory,
                _clipboardWriter,
                _theme);
            var lease = new MainPageLease(
                new MainPageIdentity(MainNavigationPage.Library, section),
                MainPageMount.LibraryBody,
                clipboardView);
            lease.Own(clipboardView);
            _pageOwner.CommitOrDispose(lease, generation, _navigation.Generation);
            return;
        }

        var body = MakeCenteredMessage(title, description);
        _pageOwner.CommitOrDispose(
            new MainPageLease(
                new MainPageIdentity(MainNavigationPage.Library, section),
                MainPageMount.LibraryBody,
                body),
            generation,
            _navigation.Generation);
    }

    private void ShowUnifiedSearch()
    {
        PrepareForNavigation();
        _navigation.ShowPage(MainNavigationPage.Search);
        Select(_topButtons, MainNavigationPage.Search);
        var searchView = new UnifiedSearchView(
            _unifiedSearch,
            _shotAssets,
            _clipboardWriter,
            _theme);
        var lease = new MainPageLease(
            new MainPageIdentity(MainNavigationPage.Search),
            MainPageMount.Destination,
            searchView);
        lease.Own(searchView);
        _pageOwner.CommitOrDispose(lease, _navigation.Generation, _navigation.Generation);
    }

    private void ShowShortcutSettings()
    {
        PrepareForNavigation();
        _navigation.ShowPage(MainNavigationPage.Settings);
        Select(_topButtons, MainNavigationPage.Settings);
        var settingsView = new ShortcutSettingsView(
            _shortcutSettings,
            _virtualDisplayCapture)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var lease = new MainPageLease(
            new MainPageIdentity(MainNavigationPage.Settings),
            MainPageMount.Destination,
            settingsView);
        lease.Own(settingsView);
        _pageOwner.CommitOrDispose(lease, _navigation.Generation, _navigation.Generation);
    }

    private void ShowApplications()
    {
        PrepareForNavigation();
        Select(_topButtons, MainNavigationPage.Apps);
        _navigation.ShowPage(MainNavigationPage.Apps);
        var applicationsView = new ApplicationsWorkspaceView(
            _applicationsWorkspaceControllers.Create(),
            _shotAssets,
            _galleryCommands,
            _theme);
        applicationsView.PreviewRequested += OpenPreview;
        applicationsView.EditRequested += OpenEditor;
        var lease = new MainPageLease(
            new MainPageIdentity(MainNavigationPage.Apps),
            MainPageMount.Destination,
            applicationsView);
        lease.Own(applicationsView);
        lease.OnDispose(() => applicationsView.PreviewRequested -= OpenPreview);
        lease.OnDispose(() => applicationsView.EditRequested -= OpenEditor);
        if (_pageOwner.CommitOrDispose(
                lease,
                _navigation.Generation,
                _navigation.Generation))
        {
            applicationsView.Start();
        }
    }

    public void ShowLibraryPage()
    {
        ShowFromTray();
        ShowLibrary();
    }

    private void PrepareForNavigation()
    {
        _libraryWorkspaceController.CancelCurrent();
        ClosePreview(refreshSource: false);
        CloseEditor(refreshSource: false);
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
