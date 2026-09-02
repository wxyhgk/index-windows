using Index.Storage;

namespace Index.Tests;

public sealed class CapturedApplicationsWorkspaceControllerTests
{
    [Fact]
    public async Task FactoryCreatesIndependentWorkspaceLifetimes()
    {
        var source = new FakeSource
        {
            Applications = [App("Edge", "edge.exe", 1, 1)]
        };
        var factory = new CapturedApplicationsWorkspaceControllerFactory(source);

        using var first = factory.Create();
        using var second = factory.Create();
        first.Dispose();
        await second.LoadOverviewAsync();

        Assert.NotSame(first, second);
        Assert.Equal(CapturedApplicationsWorkspaceStatus.Overview, second.State.Status);
    }

    [Fact]
    public async Task LoadOverviewPublishesLoadingThenFilteredOverview()
    {
        var source = new FakeSource
        {
            Applications = [
                App("Edge", "edge.exe", 4, 2),
                App("Explorer", "explorer.exe", 2, 1)]
        };
        using var controller = new CapturedApplicationsWorkspaceController(source);
        var statuses = new List<CapturedApplicationsWorkspaceStatus>();
        controller.StateChanged += state => statuses.Add(state.Status);

        await controller.LoadOverviewAsync();

        Assert.Equal(
            [CapturedApplicationsWorkspaceStatus.Loading,
             CapturedApplicationsWorkspaceStatus.Overview],
            statuses);
        var state = controller.State;
        Assert.Equal(["Edge", "Explorer"], state.Overview!.All.Select(app => app.Name));
        Assert.Equal(1, source.ApplicationQueryCount);
    }

    [Fact]
    public async Task SearchAndSortRecomputeCachedOverviewWithoutQueryingSource()
    {
        var source = new FakeSource
        {
            Applications = [
                App("Edge", "edge.exe", 2, 2),
                App("Explorer", "explorer.exe", 5, 1)]
        };
        using var controller = new CapturedApplicationsWorkspaceController(source);
        await controller.LoadOverviewAsync();

        controller.SetSearchQuery("edge");
        controller.SetSort(CapturedApplicationSort.Name);

        Assert.Equal(1, source.ApplicationQueryCount);
        var state = controller.State;
        Assert.Equal("edge", state.SearchQuery);
        Assert.Equal(CapturedApplicationSort.Name, state.Sort);
        Assert.Equal("Edge", Assert.Single(state.Overview!.All).Name);
        Assert.Empty(state.Overview.Recent);
        Assert.Empty(state.Overview.Frequent);
    }

    [Fact]
    public async Task OpenApplicationPublishesSelectedApplicationAndFirstPage()
    {
        var edge = App("Edge", "edge.exe", 4, 2);
        var firstPage = new ShotPage([Shot(42, edge)], null);
        var source = new FakeSource
        {
            Applications = [edge],
            ApplicationPage = firstPage
        };
        using var controller = new CapturedApplicationsWorkspaceController(source);

        await controller.OpenApplicationAsync(edge.StableId);

        var state = controller.State;
        Assert.Equal(CapturedApplicationsWorkspaceStatus.Application, state.Status);
        Assert.Same(edge, state.SelectedApplication);
        Assert.Same(firstPage, state.FirstPage);
        Assert.Equal(edge.Identity, source.LastPageApplication);
    }

    [Fact]
    public async Task BackToOverviewCancelsDetailLoadAndRejectsItsLateResult()
    {
        var edge = App("Edge", "edge.exe", 4, 2);
        var source = new FakeSource { Applications = [edge] };
        using var controller = new CapturedApplicationsWorkspaceController(source);
        await controller.LoadOverviewAsync();
        var pageStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePage = new TaskCompletionSource<ShotPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken pageToken = default;
        source.PageQuery = (_, token) =>
        {
            pageToken = token;
            pageStarted.SetResult();
            return releasePage.Task;
        };

        var open = controller.OpenApplicationAsync(edge.StableId);
        await pageStarted.Task;
        controller.BackToOverview();
        Assert.True(pageToken.IsCancellationRequested);
        releasePage.SetResult(new ShotPage([Shot(42, edge)], null));
        await open;

        var state = controller.State;
        Assert.Equal(CapturedApplicationsWorkspaceStatus.Overview, state.Status);
        Assert.Null(state.SelectedApplicationId);
        Assert.Equal("Edge", Assert.Single(state.Overview!.All).Name);
    }

    [Fact]
    public async Task SourceFailureBecomesErrorStateAndRefreshCanRecover()
    {
        var source = new FakeSource
        {
            ApplicationsQuery = _ => throw new InvalidOperationException("database unavailable")
        };
        using var controller = new CapturedApplicationsWorkspaceController(source);

        await controller.LoadOverviewAsync();

        Assert.Equal(CapturedApplicationsWorkspaceStatus.Error, controller.State.Status);
        Assert.Equal("database unavailable", controller.State.Error);
        source.ApplicationsQuery = _ => Task.FromResult<IReadOnlyList<CapturedApplicationSummary>>([]);
        await controller.RefreshAsync();
        Assert.Equal(CapturedApplicationsWorkspaceStatus.Overview, controller.State.Status);
    }

    [Fact]
    public async Task CallerCancellationPublishesCancelledState()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeSource
        {
            ApplicationsQuery = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return [];
            }
        };
        using var controller = new CapturedApplicationsWorkspaceController(source);
        using var cancellation = new CancellationTokenSource();

        var load = controller.LoadOverviewAsync(cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await load;

        Assert.Equal(CapturedApplicationsWorkspaceStatus.Cancelled, controller.State.Status);
    }

    [Fact]
    public async Task ThrowingObserverDoesNotCorruptSuccessfulLoadOrBlockOtherObservers()
    {
        var source = new FakeSource { Applications = [App("Edge", "edge.exe", 1, 1)] };
        using var controller = new CapturedApplicationsWorkspaceController(source);
        var observed = new List<CapturedApplicationsWorkspaceStatus>();
        controller.StateChanged += _ => throw new InvalidOperationException("UI failed");
        controller.StateChanged += state => observed.Add(state.Status);

        await controller.LoadOverviewAsync();

        Assert.Equal(CapturedApplicationsWorkspaceStatus.Overview, controller.State.Status);
        Assert.Equal(
            [CapturedApplicationsWorkspaceStatus.Loading,
             CapturedApplicationsWorkspaceStatus.Overview],
            observed);
        await controller.RefreshAsync();
        Assert.Equal(CapturedApplicationsWorkspaceStatus.Overview, controller.State.Status);
    }

    private static CapturedApplicationSummary App(
        string name,
        string identifier,
        int count,
        int minute) => new(
        new CapturedApplicationIdentity(name, identifier),
        count,
        new DateTimeOffset(2026, 9, 2, 10, minute, 0, TimeSpan.Zero),
        []);

    private static ShotRecord Shot(long id, CapturedApplicationSummary application) => new(
        id,
        $"sha-{id}",
        application.LastCapturedAt,
        100,
        100,
        1,
        application.Name,
        application.AppIdentifier,
        null,
        null,
        null,
        null,
        0,
        0,
        100,
        100,
        "png");

    private sealed class FakeSource : IShotApplicationSource
    {
        public IReadOnlyList<CapturedApplicationSummary> Applications { get; set; } = [];
        public ShotPage ApplicationPage { get; set; } = new([], null);
        public Func<CancellationToken, Task<IReadOnlyList<CapturedApplicationSummary>>>? ApplicationsQuery { get; set; }
        public Func<CapturedApplicationIdentity, CancellationToken, Task<ShotPage>>? PageQuery { get; set; }
        public CapturedApplicationIdentity? LastPageApplication { get; private set; }
        public int ApplicationQueryCount { get; private set; }

        public Task<IReadOnlyList<CapturedApplicationSummary>> GetCapturedApplicationsAsync(
            int previewLimit = 3,
            CancellationToken cancellationToken = default)
        {
            ApplicationQueryCount++;
            return ApplicationsQuery?.Invoke(cancellationToken)
                ?? Task.FromResult(Applications);
        }

        public Task<ShotPage> GetApplicationPageAsync(
            CapturedApplicationIdentity application,
            int limit = 300,
            ShotPageCursor? cursor = null,
            CancellationToken cancellationToken = default)
        {
            LastPageApplication = application;
            return PageQuery?.Invoke(application, cancellationToken)
                ?? Task.FromResult(ApplicationPage);
        }
    }
}
