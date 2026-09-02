using Index.Preview;

namespace Index.Tests;

public sealed class PreviewSessionControllerTests
{
    [Fact]
    public void SwitchingShotsCancelsAndInvalidatesPreviousSession()
    {
        using var controller = new PreviewSessionController();
        var first = controller.Begin(11);

        var second = controller.Begin(22);

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(controller.IsCurrent(first));
        Assert.True(controller.IsCurrent(second));
        Assert.Equal(22, second.ShotId);
        Assert.True(second.Generation > first.Generation);
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    [Fact]
    public async Task RapidSwitchCancelsInFlightWork()
    {
        using var controller = new PreviewSessionController();
        var first = controller.Begin(11);
        var inFlight = Task.Delay(Timeout.InfiniteTimeSpan, first.CancellationToken);

        var second = controller.Begin(22);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inFlight);
        Assert.True(controller.IsCurrent(second));
    }

    [Fact]
    public void DeactivateCancelsCurrentSession()
    {
        using var controller = new PreviewSessionController();
        var session = controller.Begin(11);

        controller.Deactivate();

        Assert.True(session.CancellationToken.IsCancellationRequested);
        Assert.False(controller.IsCurrent(session));
    }

    [Fact]
    public void CurrentCheckRejectsEveryMismatchedIdentityPart()
    {
        using var controller = new PreviewSessionController();
        var session = controller.Begin(11);

        Assert.False(controller.IsCurrent(session with
        {
            SessionId = Guid.NewGuid()
        }));
        Assert.False(controller.IsCurrent(session with
        {
            ShotId = session.ShotId + 1
        }));
        Assert.False(controller.IsCurrent(session with
        {
            Generation = session.Generation + 1
        }));
        Assert.True(controller.IsCurrent(session));
    }

    [Fact]
    public void DisposeCancelsCurrentAndRejectsFutureSessions()
    {
        var controller = new PreviewSessionController();
        var session = controller.Begin(11);

        controller.Dispose();

        Assert.True(session.CancellationToken.IsCancellationRequested);
        Assert.False(controller.IsCurrent(session));
        Assert.Throws<ObjectDisposedException>(() => controller.Begin(22));
    }

    [Fact]
    public void RepeatedDeactivateAndDisposeAreSafe()
    {
        var controller = new PreviewSessionController();
        var session = controller.Begin(11);

        controller.Deactivate();
        controller.Deactivate();
        controller.Dispose();
        controller.Dispose();

        Assert.True(session.CancellationToken.IsCancellationRequested);
    }
}
