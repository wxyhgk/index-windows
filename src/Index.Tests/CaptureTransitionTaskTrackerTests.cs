using Index.Capture;

namespace Index.Tests;

public sealed class CaptureTransitionTaskTrackerTests
{
    [Fact]
    public async Task WaitCompletesAfterTrackedTransitionFinishes()
    {
        var tracker = new CaptureTransitionTaskTracker();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tracker.Track(() => completion.Task);

        Assert.True(tracker.HasActiveTransition);
        completion.SetResult();

        Assert.True(await tracker.WaitForCompletionAsync(TimeSpan.FromSeconds(1)));
        await WaitUntilInactiveAsync(tracker);
    }

    [Fact]
    public async Task WaitReturnsFalseWhenRestoreDoesNotFinishWithinBound()
    {
        var tracker = new CaptureTransitionTaskTracker();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tracker.Track(() => completion.Task);

        Assert.False(await tracker.WaitForCompletionAsync(TimeSpan.FromMilliseconds(10)));

        completion.SetResult();
        await WaitUntilInactiveAsync(tracker);
    }

    [Fact]
    public async Task OlderCompletionDoesNotClearNewerTransition()
    {
        var tracker = new CaptureTransitionTaskTracker();
        var first = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = tracker.Track(() => first.Task);
        _ = tracker.Track(() => second.Task);

        first.SetResult();
        await first.Task;

        Assert.True(tracker.HasActiveTransition);
        second.SetResult();
        Assert.True(await tracker.WaitForCompletionAsync(TimeSpan.FromSeconds(1)));
        await WaitUntilInactiveAsync(tracker);
    }

    [Fact]
    public async Task FaultedTransitionCountsAsCompletedForShutdown()
    {
        var tracker = new CaptureTransitionTaskTracker();
        _ = tracker.Track(() =>
            Task.FromException(new InvalidOperationException("failed")));

        Assert.True(await tracker.WaitForCompletionAsync(TimeSpan.FromSeconds(1)));
        await WaitUntilInactiveAsync(tracker);
    }

    [Fact]
    public async Task WaitWithoutTransitionCompletesImmediately()
    {
        var tracker = new CaptureTransitionTaskTracker();

        Assert.True(await tracker.WaitForCompletionAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task CloseSnapshotsActiveTransitionAndRejectsLaterWork()
    {
        var tracker = new CaptureTransitionTaskTracker();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var active = tracker.Track(() => completion.Task);

        var shutdown = tracker.CloseAndGetCurrent();
        var accepted = tracker.TryTrack(() => Task.CompletedTask, out var rejected);

        Assert.Same(active, shutdown);
        Assert.False(accepted);
        Assert.Same(Task.CompletedTask, rejected);
        var error = Record.Exception(() =>
        {
            _ = tracker.Track(() => Task.CompletedTask);
        });
        Assert.IsType<InvalidOperationException>(error);

        completion.SetResult();
        await shutdown;
    }

    private static async Task WaitUntilInactiveAsync(
        CaptureTransitionTaskTracker tracker)
    {
        for (var attempt = 0; attempt < 20 && tracker.HasActiveTransition; attempt++)
            await Task.Delay(5);

        Assert.False(tracker.HasActiveTransition);
    }
}
