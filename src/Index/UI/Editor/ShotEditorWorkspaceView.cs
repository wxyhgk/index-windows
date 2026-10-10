using Index.Editor;
using Index.Gallery;
using Index.Storage;
using Index.UI.Gallery;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Index.UI.Editor;

/// <summary>
/// Single-window editor shell. Domain state remains in ShotEditorSessionController;
/// child views own canvas, toolbar, inspector, history and filmstrip presentation.
/// </summary>
internal sealed class ShotEditorWorkspaceView : UserControl, IDisposable
{
    private readonly ShotEditorSessionController _controller;
    private readonly GalleryShotCommandService _commands;
    private readonly GalleryTheme _theme;
    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly EditorCanvasPane _canvasPane;
    private readonly EditorToolbarHostView _toolbarHost;
    private readonly EditorLayersInspectorView _inspector;
    private readonly EditorRevisionHistoryView _history;
    private readonly EditorFilmstripView _filmstrip;
    private readonly EditorFooterView _footer;
    private readonly TextBlock _status = new();
    private bool _loadStarted;
    private bool _loaded;
    private bool _canEdit;
    private bool _navigationInProgress;
    private bool _disposed;
    private DateTimeOffset? _reportedSavedAt;

    public ShotEditorWorkspaceView(
        ShotEditorSessionController controller,
        IShotPageSource shotSource,
        IShotAssetReader assets,
        GalleryShotCommandService commands,
        GalleryTheme theme)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ArgumentNullException.ThrowIfNull(shotSource);
        ArgumentNullException.ThrowIfNull(assets);
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _controller.SetSessionDispatcher(RunOnUiAsync);

        _canvasPane = new EditorCanvasPane(_controller.Annotation, _controller.Shot, _theme);
        _toolbarHost = new EditorToolbarHostView(_controller.Annotation);
        _inspector = new EditorLayersInspectorView(_controller.Annotation, _theme);
        _history = new EditorRevisionHistoryView(_theme);
        _filmstrip = new EditorFilmstripView(
            new EditorFilmstripController(shotSource, _controller.Shot),
            assets,
            _theme);
        _footer = new EditorFooterView(_theme) { ZoomPercent = 100 };

        Content = BuildLayout();
        _controller.StateChanged += OnControllerStateChanged;
        _controller.SaveFailed += OnSaveFailed;
        _canvasPane.ZoomChanged += OnZoomChanged;
        _footer.ZoomOutRequested += OnZoomOutRequested;
        _footer.FitRequested += OnFitRequested;
        _footer.ZoomInRequested += OnZoomInRequested;
        _footer.CopyRequested += OnCopyRequested;
        _footer.ExportRequested += OnExportRequested;
        _history.RevisionRequested += OnRevisionRequested;
        _filmstrip.ShotSelected += OnFilmstripShotSelected;
        Loaded += OnLoaded;
    }

    public event Action<ShotRecord>? CloseRequested;
    public event Action<ShotRecord>? ShotSwitchRequested;
    public event Action<ShotRecord>? RevisionSaved;

    private FrameworkElement BuildLayout()
    {
        var root = new Grid { Background = _theme.Panel };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(BuildHeader());

        var workspace = new Grid { ColumnSpacing = 12, Padding = new Thickness(12) };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
        workspace.Children.Add(_canvasPane);
        var rail = new Grid { RowSpacing = 10 };
        rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        rail.Children.Add(_inspector);
        Grid.SetRow(_history, 1);
        rail.Children.Add(_history);
        Grid.SetColumn(rail, 1);
        workspace.Children.Add(rail);
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        Grid.SetRow(_filmstrip, 2);
        root.Children.Add(_filmstrip);
        Grid.SetRow(_footer, 3);
        root.Children.Add(_footer);
        return root;
    }

    private FrameworkElement BuildHeader()
    {
        var header = new Grid
        {
            MinHeight = 76,
            Padding = new Thickness(14, 8, 14, 8),
            Background = _theme.Card
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = MakeButton("返回图库", "\uE72B");
        back.Click += OnBackClicked;
        header.Children.Add(back);
        Grid.SetColumn(_toolbarHost, 1);
        header.Children.Add(_toolbarHost);

        _status.Text = "正在读取…";
        _status.FontSize = 11;
        _status.Foreground = _theme.Muted;
        var title = new StackPanel
        {
            Width = 210,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = _controller.Shot.WindowTitle ?? $"截图 {_controller.Shot.Id}",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = _theme.Text,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                _status
            }
        };
        Grid.SetColumn(title, 2);
        header.Children.Add(title);
        return header;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_loadStarted || _disposed)
            return;
        _loadStarted = true;
        try
        {
            var result = await _controller.LoadAsync(_lifetimeCancellation.Token);
            await RunOnUiAsync(() => ApplyLoadResult(result), _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await TryShowFailureAsync("编辑器加载失败", error);
        }
    }

    private void ApplyLoadResult(ShotEditorLoadResult result)
    {
        if (_disposed)
            return;
        if (result.BasePng.IsEmpty)
        {
            _status.Text = result.Status == ShotEditorLoadStatus.Corrupt
                ? "图片损坏，无法编辑"
                : "原图缺失，无法编辑";
            _footer.Warning = result.Warning ?? "没有可用的图片数据";
            return;
        }

        _loaded = true;
        _canEdit = result.CanEdit;
        _canvasPane.SetImage(result.BasePng.ToArray(), _canEdit);
        _toolbarHost.SetEditingEnabled(_canEdit);
        _inspector.IsReadOnly = !_canEdit;
        _footer.Warning = result.Warning ?? "原图保持不变，标注会自动保存为新版本";
        UpdateFromController();
    }

    private async void OnBackClicked(object sender, RoutedEventArgs args)
    {
        if (_navigationInProgress)
            return;
        _navigationInProgress = true;
        SetEditingInteractionEnabled(false);
        try
        {
            _status.Text = "正在保存…";
            await _controller.FlushAsync(_lifetimeCancellation.Token);
            await RunOnUiAsync(() =>
            {
                if (!_disposed)
                    CloseRequested?.Invoke(_controller.Shot);
            }, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _navigationInProgress = false;
            SetEditingInteractionEnabled(true);
            await TryShowFailureAsync("保存失败", error);
        }
    }

    private async void OnFilmstripShotSelected(ShotRecord shot)
    {
        if (_navigationInProgress || _disposed || shot.Id == _controller.Shot.Id)
            return;
        _navigationInProgress = true;
        SetEditingInteractionEnabled(false);
        try
        {
            _status.Text = "正在保存并切换…";
            await _controller.FlushAsync(_lifetimeCancellation.Token);
            await RunOnUiAsync(() =>
            {
                if (!_disposed)
                    ShotSwitchRequested?.Invoke(shot);
            }, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _navigationInProgress = false;
            SetEditingInteractionEnabled(true);
            await TryShowFailureAsync("切换图片失败", error);
        }
    }

    private async void OnRevisionRequested(long revisionId)
    {
        if (_navigationInProgress || _disposed || revisionId == _controller.CurrentRevisionId)
            return;
        _navigationInProgress = true;
        SetEditingInteractionEnabled(false);
        try
        {
            _status.Text = "正在切换版本…";
            await _controller.FlushAsync(_lifetimeCancellation.Token);
            await _controller.SelectRevisionAsync(revisionId, _lifetimeCancellation.Token);
            await RunOnUiAsync(UpdateFromController, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await TryShowFailureAsync("版本切换失败", error);
        }
        finally
        {
            _navigationInProgress = false;
            SetEditingInteractionEnabled(true);
        }
    }

    private void OnCopyRequested() => _ = RunOutputAsync(
        () => _commands.CopyAsync(_controller.Shot, _lifetimeCancellation.Token),
        "已复制包含标注的图片");

    private void OnExportRequested() => _ = RunOutputAsync(
        () => _commands.ExportAsync(_controller.Shot, _lifetimeCancellation.Token),
        "已导出包含标注的 PNG");

    private async Task RunOutputAsync(Func<Task> output, string success)
    {
        if (_navigationInProgress || _disposed)
            return;
        _navigationInProgress = true;
        SetEditingInteractionEnabled(false);
        try
        {
            _status.Text = "正在保存…";
            await _controller.FlushAsync(_lifetimeCancellation.Token);
            await output();
            await RunOnUiAsync(() =>
            {
                if (!_disposed)
                    _status.Text = success;
            }, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await TryShowFailureAsync("操作失败", error);
        }
        finally
        {
            _navigationInProgress = false;
            SetEditingInteractionEnabled(true);
        }
    }

    private void OnControllerStateChanged()
    {
        if (_disposed)
            return;
        if (_dispatcher.HasThreadAccess)
            UpdateFromController();
        else if (!_dispatcher.TryEnqueue(UpdateFromController))
            System.Diagnostics.Debug.WriteLine("Editor state dispatcher is unavailable.");
    }

    private void OnSaveFailed(Exception error)
    {
        if (_disposed)
            return;
        if (!_dispatcher.TryEnqueue(() =>
            {
                if (_disposed)
                    return;
                _status.Text = "自动保存失败";
                _footer.Warning = error.Message;
            }))
        {
            System.Diagnostics.Debug.WriteLine($"Editor save failure could not be displayed: {error}");
        }
    }

    private void UpdateFromController()
    {
        if (_disposed || !_loaded)
            return;
        _toolbarHost.Refresh();
        _history.Render(_controller.RevisionHistory, _controller.CurrentRevisionId);
        _status.Text = !_canEdit
            ? "只读预览"
            : _controller.HasPendingChanges
                ? "等待自动保存…"
                : _controller.SavedAt is null
                    ? "已是最新版本"
                    : "已保存";
        if (_controller.SavedAt is { } savedAt && savedAt != _reportedSavedAt)
        {
            _reportedSavedAt = savedAt;
            RevisionSaved?.Invoke(_controller.Shot);
        }
    }

    private async Task TryShowFailureAsync(string status, Exception error)
    {
        try
        {
            await RunOnUiAsync(() =>
            {
                if (_disposed)
                    return;
                _status.Text = status;
                _footer.Warning = error.Message;
            }, CancellationToken.None);
        }
        catch
        {
        }
    }

    private void OnZoomChanged(int zoomPercent) => _footer.ZoomPercent = zoomPercent;
    private void OnZoomOutRequested() => _canvasPane.ZoomBy(0.8f);
    private void OnFitRequested() => _canvasPane.Fit();
    private void OnZoomInRequested() => _canvasPane.ZoomBy(1.25f);

    private void SetEditingInteractionEnabled(bool enabled)
    {
        if (_disposed)
            return;
        bool canInteract = enabled && _canEdit;
        _canvasPane.Canvas.IsEnabled = canInteract;
        _toolbarHost.SetEditingEnabled(canInteract);
        _inspector.IsReadOnly = !canInteract;
    }

    private Task RunOnUiAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        if (_dispatcher.HasThreadAccess)
            return operation();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await operation();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException(
                "The editor UI dispatcher is shutting down."));
        }
        return AwaitDispatchCompletionAsync(completion.Task, cancellationRegistration);
    }

    private Task RunOnUiAsync(Action operation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        if (_dispatcher.HasThreadAccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    operation();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException(
                "The editor UI dispatcher is shutting down."));
        }
        return AwaitDispatchCompletionAsync(completion.Task, cancellationRegistration);
    }

    private static async Task AwaitDispatchCompletionAsync(
        Task completion,
        CancellationTokenRegistration cancellationRegistration)
    {
        using (cancellationRegistration)
            await completion.ConfigureAwait(false);
    }

    private Button MakeButton(string title, string glyph) => new()
    {
        MinHeight = 34,
        Padding = new Thickness(10, 5, 10, 5),
        Background = _theme.Panel,
        BorderBrush = _theme.CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new FontIcon
                {
                    Glyph = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 14,
                    Foreground = _theme.Text
                },
                new TextBlock
                {
                    Text = title,
                    FontSize = 12,
                    Foreground = _theme.Text,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        }
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Loaded -= OnLoaded;
        _controller.StateChanged -= OnControllerStateChanged;
        _controller.SaveFailed -= OnSaveFailed;
        _canvasPane.ZoomChanged -= OnZoomChanged;
        _footer.ZoomOutRequested -= OnZoomOutRequested;
        _footer.FitRequested -= OnFitRequested;
        _footer.ZoomInRequested -= OnZoomInRequested;
        _footer.CopyRequested -= OnCopyRequested;
        _footer.ExportRequested -= OnExportRequested;
        _history.RevisionRequested -= OnRevisionRequested;
        _filmstrip.ShotSelected -= OnFilmstripShotSelected;

        _lifetimeCancellation.Cancel();
        _filmstrip.Dispose();
        _inspector.Dispose();
        _toolbarHost.Dispose();
        _canvasPane.Dispose();
        try
        {
            // Disposal may reconcile one final append-only revision. Run the storage
            // work without a WinUI context so synchronous page teardown cannot deadlock.
            Task.Run(() => _controller.DisposeAsync().AsTask())
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Editor final save failed: {error}");
        }
        _lifetimeCancellation.Dispose();
    }
}
