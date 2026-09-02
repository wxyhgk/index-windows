using System.Diagnostics;
using Index.Capture;

namespace Index.Tests;

public sealed class CaptureShutdownCoordinatorTests
{
    [Fact]
    public async Task WaitsForParticipantsInParallelUnderOneBudget()
    {
        var stopwatch = Stopwatch.StartNew();
        var report = await CaptureShutdownCoordinator.WaitAsync(
        [
            new CaptureShutdownWork("first", Task.Delay(40)),
            new CaptureShutdownWork("second", Task.Delay(80))
        ], TimeSpan.FromSeconds(1));

        Assert.True(report.CompletedWithinBudget);
        Assert.Empty(report.Pending);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(180));
    }

    [Fact]
    public async Task TimeoutReportsPendingWithoutCancelingRestoration()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var report = await CaptureShutdownCoordinator.WaitAsync(
        [
            new CaptureShutdownWork("stuck", completion.Task),
            new CaptureShutdownWork("done", Task.CompletedTask)
        ], TimeSpan.FromMilliseconds(10));

        Assert.False(report.CompletedWithinBudget);
        Assert.Equal(["stuck"], report.Pending);
        Assert.False(completion.Task.IsCanceled);
        completion.SetResult();
    }

    [Fact]
    public async Task FaultIsObservedWithoutBlockingOtherRestoration()
    {
        var other = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = CaptureShutdownCoordinator.WaitAsync(
        [
            new CaptureShutdownWork(
                "failed",
                Task.FromException(new InvalidOperationException("boom"))),
            new CaptureShutdownWork("other", other.Task)
        ], TimeSpan.FromSeconds(1));

        other.SetResult();
        var report = await wait;

        Assert.True(report.CompletedWithinBudget);
        var failure = Assert.Single(report.Failures);
        Assert.Equal("failed", failure.Name);
        Assert.IsType<InvalidOperationException>(failure.Error);
    }

    [Fact]
    public async Task RejectsDuplicateParticipantNames()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CaptureShutdownCoordinator.WaitAsync(
            [
                new CaptureShutdownWork("same", Task.CompletedTask),
                new CaptureShutdownWork("same", Task.CompletedTask)
            ], TimeSpan.FromSeconds(1)));
    }
}
