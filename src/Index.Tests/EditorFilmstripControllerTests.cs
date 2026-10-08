using Index.Editor;
using Index.Storage;

namespace Index.Tests;

public sealed class EditorFilmstripControllerTests
{
    [Fact]
    public async Task ReloadFindsCurrentAcrossCursorPagesAndPreservesSourceOrder()
    {
        var newest = Shot(3);
        var middle = Shot(2);
        var oldest = Shot(1);
        var cursor = new ShotPageCursor(middle.CapturedAt, middle.Id);
        var source = new FakePageSource((_, pageCursor, _) => Task.FromResult(
            pageCursor is null
                ? new ShotPage([newest, middle], cursor)
                : new ShotPage([oldest], null)));
        using var controller = new EditorFilmstripController(source, oldest, pageSize: 2);

        await controller.ReloadAsync();

        Assert.Equal([3, 2, 1], controller.State.Items.Select(item => item.Id));
        Assert.Equal(2, controller.State.CurrentIndex);
        Assert.Equal(1, controller.State.CurrentShotId);
        Assert.False(controller.State.IsLoading);
    }

    [Fact]
    public async Task AdjacentCandidateDoesNotCommitCurrentBeforeHostFlushes()
    {
        var current = Shot(2);
        var older = Shot(1);
        var source = new FakePageSource((_, _, _) => Task.FromResult(
            new ShotPage([current, older], null)));
        using var controller = new EditorFilmstripController(source, current);
        await controller.ReloadAsync();

        var candidate = await controller.GetAdjacentAsync(1);

        Assert.Equal(older, candidate);
        Assert.Equal(current.Id, controller.State.CurrentShotId);
        controller.SetCurrentShot(candidate!);
        Assert.Equal(older.Id, controller.State.CurrentShotId);
    }

    [Fact]
    public async Task AdjacentLoadsOlderPageAtLoadedBoundary()
    {
        var current = Shot(2);
        var older = Shot(1);
        var cursor = new ShotPageCursor(current.CapturedAt, current.Id);
        var source = new FakePageSource((_, pageCursor, _) => Task.FromResult(
            pageCursor is null
                ? new ShotPage([current], cursor)
                : new ShotPage([older], null)));
        using var controller = new EditorFilmstripController(source, current);
        await controller.ReloadAsync();

        var candidate = await controller.GetAdjacentAsync(1);

        Assert.Equal(older.Id, candidate?.Id);
        Assert.Equal([2, 1], controller.State.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task StaleReloadCannotReplaceNewerGeneration()
    {
        var current = Shot(2);
        var stale = new TaskCompletionSource<ShotPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var source = new FakePageSource((_, _, _) =>
        {
            calls++;
            return calls == 1
                ? stale.Task
                : Task.FromResult(new ShotPage([current, Shot(1)], null));
        });
        using var controller = new EditorFilmstripController(source, current);

        var first = controller.ReloadAsync();
        var second = controller.ReloadAsync();
        await second;
        stale.SetResult(new ShotPage([current, Shot(99)], null));
        await first;

        Assert.Equal([2, 1], controller.State.Items.Select(item => item.Id));
        Assert.False(controller.State.IsLoading);
    }

    [Theory]
    [InlineData(100000, 1, 200, 100, 200, 0.002)]
    [InlineData(1, 100000, 200, 100, 0.001, 100)]
    [InlineData(400, 200, 200, 100, 200, 100)]
    public void ThumbnailFitPreservesAspectRatioForExtremeImages(
        double sourceWidth,
        double sourceHeight,
        double boundsWidth,
        double boundsHeight,
        double expectedWidth,
        double expectedHeight)
    {
        var layout = EditorThumbnailFit.Calculate(
            sourceWidth,
            sourceHeight,
            boundsWidth,
            boundsHeight);

        Assert.Equal(expectedWidth, layout.Width, 6);
        Assert.Equal(expectedHeight, layout.Height, 6);
        Assert.Equal(sourceWidth / sourceHeight, layout.Width / layout.Height, 6);
        Assert.Equal((boundsWidth - layout.Width) / 2, layout.X, 6);
        Assert.Equal((boundsHeight - layout.Height) / 2, layout.Y, 6);
    }

    private static ShotRecord Shot(long id) => new(
        id,
        $"sha-{id}",
        DateTimeOffset.UnixEpoch.AddSeconds(id),
        1920,
        1080,
        1,
        "App",
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        1920,
        1080,
        "png");

    private sealed class FakePageSource(
        Func<int, ShotPageCursor?, CancellationToken, Task<ShotPage>> query) : IShotPageSource
    {
        public Task<ShotPage> GetPageAsync(
            int limit = 300,
            ShotPageCursor? cursor = null,
            CancellationToken cancellationToken = default)
            => query(limit, cursor, cancellationToken);
    }
}
