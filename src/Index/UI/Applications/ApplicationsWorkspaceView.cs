using Index.Storage;
using Index.UI.Gallery;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Index.UI.Applications;

/// <summary>Single-window applications overview and per-application gallery workspace.</summary>
internal sealed class ApplicationsWorkspaceView : UserControl, IDisposable
{
    private readonly ShotStore _store;
    private readonly IShotApplicationSource _applications;
    private readonly LibraryOrganizationStore _organization;
    private readonly IShotAssetReader _assets;
    private readonly GalleryTheme _theme;
    private readonly Grid _root = new();
    private readonly Grid _overview = new();
    private readonly ContentControl _overviewBody = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private readonly ContentControl _applicationBody = new()
    {
        Visibility = Visibility.Collapsed,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private readonly Grid _overviewHeader;
    private readonly TextBlock _overviewCount = new();
    private readonly TextBlock _searchLabel = new();
    private Button? _searchSurface;
    private ComboBox? _sort;
    private readonly List<IDisposable> _visualResources = [];
    private IReadOnlyList<CapturedApplicationSummary> _allApplications = [];
    private CapturedApplicationSort _selectedSort = CapturedApplicationSort.CaptureCount;
    private CancellationTokenSource? _loadCancellation;
    private ShotGalleryGridView? _gallery;
    private string? _selectedApplicationId;
    private string _searchQuery = string.Empty;
    private int _loadGeneration;
    private bool _shellBuilt;
    private bool _initialLoadQueued;
    private bool _disposed;

    public ApplicationsWorkspaceView(
        ShotStore store,
        LibraryOrganizationStore organization,
        IShotAssetReader assets,
        GalleryTheme theme)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _applications = store;
        _organization = organization ?? throw new ArgumentNullException(nameof(organization));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _overviewHeader = CreateOverviewHeader();
        Unloaded += OnUnloaded;
        _store.CaptureSaved += OnCaptureSaved;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = _root;
        _root.Children.Add(new TextBlock
        {
            Text = "正在整理来源应用…",
            Foreground = _theme.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    public event Action<ShotGalleryGridView, ShotRecord>? PreviewRequested;

    public void Start()
    {
        if (_disposed || _initialLoadQueued || _allApplications.Count > 0)
            return;
        _initialLoadQueued = true;
        if (DispatcherQueue.TryEnqueue(BeginInitialLoad))
            return;

        _initialLoadQueued = false;
        BuildStableShell();
        RenderFailure("无法将应用页加载任务调度到界面线程。");
    }

    private void BeginInitialLoad()
    {
        _initialLoadQueued = false;
        if (_disposed)
            return;
        BuildStableShell();
        _ = LoadAsync(_selectedApplicationId);
    }

    private void AddOverviewControls()
    {
        if (_searchSurface is not null)
            return;
        _searchLabel.Text = "搜索应用（点击后输入）";
        _searchLabel.Foreground = _theme.Muted;
        _searchSurface = new Button
        {
            Width = 230,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = _searchLabel
        };
        _searchSurface.Click += OnSearchSurfaceClicked;
        _searchSurface.CharacterReceived += OnSearchCharacterReceived;
        _searchSurface.KeyDown += OnSearchKeyDown;
        _sort = new ComboBox { MinWidth = 138 };
        _sort.Items.Add(SortItem("按截图数量", CapturedApplicationSort.CaptureCount));
        _sort.Items.Add(SortItem("按最近使用", CapturedApplicationSort.Recent));
        _sort.Items.Add(SortItem("按名称", CapturedApplicationSort.Name));
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += OnSortChanged;
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _searchSurface, _sort }
        };
        var clearSearch = new Button { Content = "清除" };
        clearSearch.Click += (_, _) => SetSearchQuery(string.Empty);
        controls.Children.Add(clearSearch);
        var refresh = new Button { Content = "刷新" };
        refresh.Click += (_, _) => _ = LoadAsync(null);
        controls.Children.Add(refresh);
        Grid.SetColumn(controls, 1);
        _overviewHeader.Children.Add(controls);
    }

    private void OnSearchSurfaceClicked(object sender, RoutedEventArgs args) =>
        _ = _searchSurface?.Focus(FocusState.Programmatic);

    private void OnSearchCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (args.Character < 0x20 || args.Character == 0x7F)
            return;
        SetSearchQuery(_searchQuery + char.ConvertFromUtf32((int)args.Character));
        args.Handled = true;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Back && _searchQuery.Length > 0)
        {
            SetSearchQuery(_searchQuery[..^1]);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            SetSearchQuery(string.Empty);
            args.Handled = true;
        }
    }

    private void SetSearchQuery(string query)
    {
        _searchQuery = query;
        _searchLabel.Text = string.IsNullOrEmpty(query)
            ? "搜索应用（点击后输入）"
            : query;
        _searchLabel.Foreground = string.IsNullOrEmpty(query) ? _theme.Muted : _theme.Text;
        if (!_disposed && _selectedApplicationId is null && _allApplications.Count > 0)
            RenderOverviewBody();
    }

    private static ComboBoxItem SortItem(string title, CapturedApplicationSort sort) => new()
    {
        Content = title,
        Tag = sort
    };

    private Grid CreateOverviewHeader()
    {
        var header = new Grid
        {
            ColumnSpacing = 14,
            Margin = new Thickness(0, 4, 0, 14)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _overviewCount.FontSize = 12;
        _overviewCount.Foreground = _theme.Muted;
        header.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "应用",
                    FontSize = 26,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = _theme.Text
                },
                _overviewCount
            }
        });
        return header;
    }

    private void BuildStableShell()
    {
        if (_shellBuilt)
            return;
        _root.Children.Clear();
        _overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _overview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _overview.Children.Add(_overviewHeader);
        Grid.SetRow(_overviewBody, 1);
        _overview.Children.Add(_overviewBody);
        _root.Children.Add(_overview);
        _root.Children.Add(_applicationBody);
        _shellBuilt = true;
        AddOverviewControls();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => CancelLoad();

    private void OnCaptureSaved(StoredCapture capture)
    {
        if (_disposed)
            return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
                _ = LoadAsync(_selectedApplicationId);
        });
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_sort?.SelectedItem is ComboBoxItem { Tag: CapturedApplicationSort sort })
            _selectedSort = sort;
        if (!_disposed && _selectedApplicationId is null && _allApplications.Count > 0)
            RenderOverviewBody();
    }

    private async Task LoadAsync(string? selectedApplicationId)
    {
        CancelLoad();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        var generation = ++_loadGeneration;
        ShowLoading(selectedApplicationId is null
            ? "正在整理来源应用…"
            : "正在读取应用截图…");
        try
        {
            var applications = await _applications.GetCapturedApplicationsAsync(
                cancellationToken: cancellation.Token);
            CapturedApplicationSummary? selected = null;
            ShotPage? firstPage = null;
            if (selectedApplicationId is not null)
            {
                selected = applications.FirstOrDefault(application =>
                    string.Equals(
                        application.StableId,
                        selectedApplicationId,
                        StringComparison.OrdinalIgnoreCase));
                if (selected is not null)
                {
                    firstPage = await _applications.GetApplicationPageAsync(
                        selected.Identity,
                        cancellationToken: cancellation.Token);
                }
            }

            if (_disposed || cancellation.IsCancellationRequested
                || generation != _loadGeneration)
                return;
            _allApplications = applications;
            if (selected is not null && firstPage is not null)
            {
                _selectedApplicationId = selected.StableId;
                RenderApplication(selected, firstPage);
            }
            else
            {
                _selectedApplicationId = null;
                RenderOverview();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_disposed && generation == _loadGeneration)
                RenderFailure(error.Message);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
                _loadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void RenderOverview()
    {
        DisposeVisualResources();
        _applicationBody.Content = null;
        _applicationBody.Visibility = Visibility.Collapsed;
        _overview.Visibility = Visibility.Visible;
        RenderOverviewBody();
    }

    private void RenderOverviewBody()
    {
        DisposeVisualResources();
        var overview = CapturedApplicationCatalog.Create(
            _allApplications,
            _searchQuery,
            _selectedSort);
        _overviewCount.Text = $"{overview.All.Count:N0} 个应用";
        if (overview.All.Count == 0)
        {
            _overviewBody.Content = CenteredMessage(
            string.IsNullOrWhiteSpace(_searchQuery) ? "还没有应用截图" : "没有匹配的应用",
                string.IsNullOrWhiteSpace(_searchQuery)
                    ? "截图后，来源应用会自动出现在这里"
                    : "换个应用名称或清空搜索词");
            return;
        }

        var sections = new StackPanel
        {
            Spacing = 24,
            Padding = new Thickness(2, 0, 10, 18),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (overview.Recent.Count > 0)
            AddSection(sections, "最近使用", overview.Recent, featured: true);
        if (overview.Frequent.Count > 0)
            AddSection(sections, "常用应用", overview.Frequent, featured: false);
        AddSection(
            sections,
            string.IsNullOrWhiteSpace(_searchQuery) ? "全部应用" : "匹配的应用",
            overview.All,
            featured: false);
        var scroll = new ScrollViewer
        {
            Content = sections,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _overviewBody.Content = scroll;
    }

    private void AddSection(
        Panel parent,
        string title,
        IReadOnlyList<CapturedApplicationSummary> applications,
        bool featured)
    {
        var section = new StackPanel
        {
            Spacing = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _theme.Text
        });
        var cards = new ResponsiveApplicationCardGrid(
            applications,
            featured,
            _assets,
            _theme,
            application => _ = LoadAsync(application.StableId));
        _visualResources.Add(cards);
        section.Children.Add(cards);
        parent.Children.Add(section);
    }

    private void RenderApplication(
        CapturedApplicationSummary application,
        ShotPage firstPage)
    {
        DisposeVisualResources();
        _overview.Visibility = Visibility.Collapsed;
        _applicationBody.Visibility = Visibility.Visible;
        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid
        {
            ColumnSpacing = 12,
            Margin = new Thickness(0, 4, 0, 14)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = new Button { Content = "‹ 应用" };
        back.Click += (_, _) =>
        {
            _selectedApplicationId = null;
            RenderOverview();
        };
        header.Children.Add(back);
        FrameworkElement icon;
        if (application.AppIdentifier is { } identifier)
        {
            var applicationIcon = new AppIconView(identifier, application.Name, 34);
            _visualResources.Add(applicationIcon);
            icon = applicationIcon;
        }
        else
        {
            icon = new FontIcon { Glyph = "\uECAA", FontSize = 26, Foreground = _theme.Muted };
        }
        Grid.SetColumn(icon, 1);
        header.Children.Add(icon);
        var labels = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = application.Name,
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = _theme.Text
                },
                new TextBlock
                {
                    Text = $"{application.CaptureCount:N0} 个项目",
                    FontSize = 12,
                    Foreground = _theme.Muted
                }
            }
        };
        Grid.SetColumn(labels, 2);
        header.Children.Add(labels);
        var refresh = new Button { Content = "刷新" };
        refresh.Click += (_, _) => _ = LoadAsync(application.StableId);
        Grid.SetColumn(refresh, 3);
        header.Children.Add(refresh);
        page.Children.Add(header);

        FrameworkElement body;
        if (firstPage.Items.Count == 0)
        {
            body = CenteredMessage("这个应用还没有截图", "返回应用列表后可刷新来源数据");
        }
        else
        {
            var pageSource = new CapturedApplicationPageSource(
                _applications,
                application.Identity);
            var gallery = new ShotGalleryGridView(
                firstPage,
                _store,
                _theme,
                pageSource);
            _gallery = gallery;
            var detail = new ShotDetailPane(
                _store,
                new GalleryShotCommands(_store, _organization, DispatcherQueue),
                _theme);
            gallery.SelectionChanged += detail.ShowShot;
            gallery.PreviewRequested += shot => PreviewRequested?.Invoke(gallery, shot);
            detail.PreviewRequested += shot => PreviewRequested?.Invoke(gallery, shot);
            detail.ShotDeleted += deletedShot => _ = LoadAsync(application.StableId);
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
        _applicationBody.Content = page;
    }

    private void ShowLoading(string message)
    {
        DisposeVisualResources();
        var loading = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, Foreground = _theme.Muted }
            }
        };
        if (_selectedApplicationId is null)
        {
            _applicationBody.Content = null;
            _applicationBody.Visibility = Visibility.Collapsed;
            _overview.Visibility = Visibility.Visible;
            _overviewBody.Content = loading;
        }
        else
        {
            _overview.Visibility = Visibility.Collapsed;
            _applicationBody.Visibility = Visibility.Visible;
            _applicationBody.Content = loading;
        }
    }

    private void RenderFailure(string message)
    {
        DisposeVisualResources();
        var panel = CenteredMessage("应用读取失败", message);
        var retry = new Button
        {
            Content = "重试",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        retry.Click += (_, _) => _ = LoadAsync(_selectedApplicationId);
        panel.Children.Add(retry);
        if (_selectedApplicationId is null)
        {
            _applicationBody.Content = null;
            _applicationBody.Visibility = Visibility.Collapsed;
            _overview.Visibility = Visibility.Visible;
            _overviewBody.Content = panel;
        }
        else
        {
            _overview.Visibility = Visibility.Collapsed;
            _applicationBody.Visibility = Visibility.Visible;
            _applicationBody.Content = panel;
        }
    }

    private StackPanel CenteredMessage(string title, string description) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 8,
        Children =
        {
            new TextBlock
            {
                Text = title,
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = _theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            new TextBlock
            {
                Text = description,
                FontSize = 13,
                Foreground = _theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        }
    };

    private void DisposeVisualResources()
    {
        _gallery?.Dispose();
        _gallery = null;
        foreach (var resource in _visualResources)
            resource.Dispose();
        _visualResources.Clear();
    }

    private void CancelLoad()
    {
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        cancellation?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLoad();
        DisposeVisualResources();
        _store.CaptureSaved -= OnCaptureSaved;
        if (_searchSurface is not null)
        {
            _searchSurface.Click -= OnSearchSurfaceClicked;
            _searchSurface.CharacterReceived -= OnSearchCharacterReceived;
            _searchSurface.KeyDown -= OnSearchKeyDown;
        }
        if (_sort is not null)
            _sort.SelectionChanged -= OnSortChanged;
        Unloaded -= OnUnloaded;
        _overviewBody.Content = null;
        _applicationBody.Content = null;
        _root.Children.Clear();
    }
}
