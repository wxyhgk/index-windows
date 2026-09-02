using Index.Storage;

namespace Index.Tests;

public sealed class LibraryWorkspaceControllerTests
{
    [Fact]
    public async Task GalleryLoadPublishesImmutableCountAndFirstPageSnapshot()
    {
        var countStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shot = Shot(42);
        var gallery = new FakeGallery
        {
            CountQuery = async _ =>
            {
                countStarted.SetResult();
                await release.Task;
                return 7;
            },
            PageQuery = async _ =>
            {
                pageStarted.SetResult();
                await release.Task;
                return new ShotPage([shot], new ShotPageCursor(shot.CapturedAt, shot.Id));
            }
        };
        using var controller = new LibraryWorkspaceController(gallery, new FakeOrganization());

        var loading = controller.LoadGalleryAsync();
        await Task.WhenAll(countStarted.Task, pageStarted.Task);
        Assert.Equal(LibraryWorkspaceStatus.Loading, controller.State.Status);
        release.SetResult();
        var state = await loading;

        Assert.Equal(LibraryWorkspaceStatus.Gallery, state.Status);
        Assert.Equal(7, state.Gallery!.TotalCount);
        Assert.Equal(shot, Assert.Single(state.Gallery.FirstPage.Items));
        Assert.NotNull(state.Gallery.FirstPage.NextCursor);
    }

    [Fact]
    public async Task CollectionsLoadReturnsFavoritesAndOrderedCollectionSnapshot()
    {
        var collection = new ShotCollectionRecord(
            3,
            "Research",
            "Pinned references",
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            0,
            2);
        var organization = new FakeOrganization
        {
            Favorites = new HashSet<long> { 2, 5 },
            Collections = [collection]
        };
        using var controller = new LibraryWorkspaceController(new FakeGallery(), organization);

        var state = await controller.LoadCollectionsAsync();

        Assert.Equal(LibraryWorkspaceStatus.Collections, state.Status);
        Assert.Equal([2L, 5L], state.Collections!.FavoriteShotIds.Order());
        Assert.Equal(collection, Assert.Single(state.Collections.Collections));
    }

    [Fact]
    public async Task NewLoadCancelsAndRejectsLatePreviousGeneration()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var countCall = 0;
        var gallery = new FakeGallery
        {
            CountQuery = _ =>
            {
                if (Interlocked.Increment(ref countCall) == 1)
                {
                    firstStarted.SetResult();
                    return releaseFirst.Task;
                }

                return Task.FromResult(2L);
            },
            PageQuery = _ => Task.FromResult(new ShotPage([], null))
        };
        using var controller = new LibraryWorkspaceController(gallery, new FakeOrganization());

        var first = controller.LoadGalleryAsync();
        await firstStarted.Task;
        var second = await controller.LoadGalleryAsync();
        releaseFirst.SetResult(99);
        var late = await first;

        Assert.Equal(LibraryWorkspaceStatus.Gallery, second.Status);
        Assert.Equal(2, second.Gallery!.TotalCount);
        Assert.Equal(LibraryWorkspaceStatus.Cancelled, late.Status);
        Assert.Equal(second, controller.State);
        Assert.True(second.Generation > late.Generation);
    }

    [Fact]
    public async Task QueryFailureBecomesErrorState()
    {
        var gallery = new FakeGallery
        {
            CountQuery = _ => throw new InvalidOperationException("database unavailable")
        };
        using var controller = new LibraryWorkspaceController(gallery, new FakeOrganization());

        var state = await controller.LoadGalleryAsync();

        Assert.Equal(LibraryWorkspaceStatus.Error, state.Status);
        Assert.Equal("database unavailable", state.Error);
        Assert.Equal(state, controller.State);
    }

    private static ShotRecord Shot(long id) => new(
        id,
        $"sha-{id}",
        new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero),
        100,
        80,
        1,
        "Index",
        "index.exe",
        "Index",
        null,
        null,
        null,
        0,
        0,
        100,
        80,
        "png");

    private sealed class FakeGallery : IShotGallerySource
    {
        public Func<CancellationToken, Task<long>>? CountQuery { get; init; }
        public Func<CancellationToken, Task<ShotPage>>? PageQuery { get; init; }

        public Task<long> GetCountAsync(CancellationToken cancellationToken = default) =>
            CountQuery?.Invoke(cancellationToken) ?? Task.FromResult(0L);

        public Task<ShotPage> GetPageAsync(
            int limit = 300,
            ShotPageCursor? cursor = null,
            CancellationToken cancellationToken = default) =>
            PageQuery?.Invoke(cancellationToken) ?? Task.FromResult(new ShotPage([], null));
    }

    private sealed class FakeOrganization : ILibraryOrganizationSource
    {
        public IReadOnlySet<long> Favorites { get; init; } = new HashSet<long>();
        public IReadOnlyList<ShotCollectionRecord> Collections { get; init; } = [];

        public Task<IReadOnlySet<long>> GetFavoriteIdsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Favorites);

        public Task<IReadOnlyList<ShotCollectionRecord>> GetCollectionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Collections);
    }
}
