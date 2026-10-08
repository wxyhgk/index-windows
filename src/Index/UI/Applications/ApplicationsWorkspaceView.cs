using Index.Gallery;
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
    private readonly CapturedApplicationsWorkspaceController _controller;
    private readonly IShotAssetReader _assets;
    private readonly GalleryShotCommandService _commands;
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
    private ShotGalleryGridView? _gallery;
    private ShotDetailPane? _detail;
    private bool _shellBuilt;
    private bool _initialLoadQueued;
    private bool _disposed;

    public ApplicationsWorkspaceView(
        CapturedApplicationsWorkspaceController controller,
        IShotAssetReader assets,
        GalleryShotCommandService commands,
        GalleryTheme theme)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _overviewHeader = CreateOverviewHeader();
        _controller.StateChanged += OnWorkspaceStateChanged;
        Unloaded += OnUnloaded;
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
    public event Action<ShotRecord, IShotPageSource>? EditRequested;

    public void Refresh()
    {
        if (!_disposed)
            Run(_controller.RefreshAsync());
    }

    public void Start()
    {
        if (_disposed || _initialLoadQueued
            || _controller.State.Status is not (
                CapturedApplicationsWorkspaceStatus.Idle
                or CapturedApplicationsWorkspaceStatus.Cancelled))
            return;
        _initialLoadQueued = true;
        if (DispatcherQueue.TryEnqueue(BeginInitialLoad))
            return;

        _initialLoadQueued = false;
        BuildStableShell();
        RenderFailure(null, "无法将应用页加载任务调度到界面线程。");
    }

    private void BeginInitialLoad()
    {
        _initialLoadQueued = false;
        if (_disposed)
            return;
        BuildStableShell();
        Run(_controller.LoadOverviewAsync());
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
        clearSearch.Click += (_, _) => _controller.SetSearchQuery(string.Empty);
        controls.Children.Add(clearSearch);
        var refresh = new Button { Content = "刷新" };
        refresh.Click += (_, _) => Run(_controller.RefreshAsync());
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
        _controller.SetSearchQuery(
            _controller.SearchQuery + char.ConvertFromUtf32((int)args.Character));
        args.Handled = true;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var query = _controller.SearchQuery;
        if (args.Key == VirtualKey.Back && query.Length > 0)
        {
            _controller.SetSearchQuery(query[..^1]);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            _controller.SetSearchQuery(string.Empty);
            args.Handled = true;
        }
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

    private void OnUnloaded(object sender, RoutedEventArgs args) =>
        _controller.CancelCurrent();

    private void OnSortChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_sort?.SelectedItem is ComboBoxItem { Tag: CapturedApplicationSort sort })
            _controller.SetSort(sort);
    }

    private void OnWorkspaceStateChanged(CapturedApplicationsWorkspaceState state)
    {
        if (_disposed)
            return;
        if (DispatcherQueue.HasThreadAccess)
        {
            RenderWorkspaceState(state);
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
                RenderWorkspaceState(_controller.State);
        });
    }

    private void RenderWorkspaceState(CapturedApplicationsWorkspaceState state)
    {
        _searchLabel.Text = string.IsNullOrEmpty(state.SearchQuery)
            ? "搜索应用（点击后输入）"
            : state.SearchQuery;
        _searchLabel.Foreground = string.IsNullOrEmpty(state.SearchQuery)
            ? _theme.Muted
            : _theme.Text;

        switch (state.Status)
        {
            case CapturedApplicationsWorkspaceStatus.Loading:
                ShowLoading(
                    state.SelectedApplicationId,
                    state.SelectedApplicationId is null
                        ? "正在整理来源应用…"
                        : "正在读取应用截图…");
                break;
            case CapturedApplicationsWorkspaceStatus.Overview when state.Overview is not null:
                RenderOverview(state.Overview, state.SearchQuery);
                break;
            case CapturedApplicationsWorkspaceStatus.Application
                when state.SelectedApplication is not null && state.FirstPage is not null:
                RenderApplication(state.SelectedApplication, state.FirstPage);
                break;
            case CapturedApplicationsWorkspaceStatus.Error:
                RenderFailure(state.SelectedApplicationId, state.Error ?? "未知错误");
                break;
        }
    }

    private void Run(Task operation) => _ = ObserveAsync(operation);

    private async Task ObserveAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception error)
        {
            if (!_disposed)
                RenderFailure(_controller.State.SelectedApplicationId, error.Message);
        }
    }

    private void RenderOverview(
        CapturedApplicationOverview overview,
        string searchQuery)
    {
        DisposeVisualResources();
        _applicationBody.Content = null;
        _applicationBody.Visibility = Visibility.Collapsed;
        _overview.Visibility = Visibility.Visible;
        _overviewCount.Text = $"{overview.All.Count:N0} 个应用";
        if (overview.All.Count == 0)
        {
            _overviewBody.Content = CenteredMessage(
            string.IsNullOrWhiteSpace(searchQuery) ? "还没有应用截图" : "没有匹配的应用",
                string.IsNullOrWhiteSpace(searchQuery)
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
            string.IsNullOrWhiteSpace(searchQuery) ? "全部应用" : "匹配的应用",
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
            application => Run(_controller.OpenApplicationAsync(application.StableId)));
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
        back.Click += (_, _) => _controller.BackToOverview();
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
        refresh.Click += (_, _) => Run(_controller.RefreshAsync());
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
            var pageSource = _controller.CreatePageSource(application.Identity);
            var gallery = new ShotGalleryGridView(
                firstPage,
                pageSource,
                _assets,
                _theme);
            _gallery = gallery;
            var detail = new ShotDetailPane(
                _assets,
                _commands,
                _theme);
            _detail = detail;
            gallery.SelectionChanged += detail.ShowShot;
            gallery.PreviewRequested += shot => PreviewRequested?.Invoke(gallery, shot);
            detail.PreviewRequested += shot => PreviewRequested?.Invoke(gallery, shot);
            detail.EditRequested += shot => EditRequested?.Invoke(shot, pageSource);
            detail.ShotDeleted += deletedShot => Run(_controller.RefreshAsync());
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

    private void ShowLoading(string? selectedApplicationId, string message)
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
        if (selectedApplicationId is null)
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

    private void RenderFailure(string? selectedApplicationId, string message)
    {
        DisposeVisualResources();
        var panel = CenteredMessage("应用读取失败", message);
        var retry = new Button
        {
            Content = "重试",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        retry.Click += (_, _) => Run(_controller.RefreshAsync());
        panel.Children.Add(retry);
        if (selectedApplicationId is null)
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
        _detail?.Dispose();
        _detail = null;
        _gallery?.Dispose();
        _gallery = null;
        foreach (var resource in _visualResources)
            resource.Dispose();
        _visualResources.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.StateChanged -= OnWorkspaceStateChanged;
        _controller.Dispose();
        DisposeVisualResources();
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
