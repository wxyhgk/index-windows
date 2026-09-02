using Index.Capture;

namespace Index.Tests;

public sealed class CaptureSessionGateTests
{
    [Fact]
    public void NormalCaptureExcludesReentryUntilEnded()
    {
        var gate = new CaptureSessionGate();

        Assert.True(gate.TryBeginCapture());
        Assert.Equal(CaptureSessionState.Capturing, gate.State);
        Assert.False(gate.TryBeginCapture());

        gate.EndCapture();

        Assert.Equal(CaptureSessionState.Idle, gate.State);
        Assert.True(gate.TryBeginCapture());
    }

    [Fact]
    public void HighResolutionTransitionRemainsInsideCaptureSession()
    {
        var gate = new CaptureSessionGate();
        Assert.True(gate.TryBeginCapture());

        Assert.True(gate.TryBeginHighResolutionTransition());

        Assert.True(gate.IsHighResolutionTransition);
        Assert.False(gate.TryBeginCapture());
        Assert.False(gate.TryBeginHighResolutionTransition());
    }

    [Fact]
    public void ExplicitFailureResumesFrozenSelectionWithoutReopeningGate()
    {
        var gate = HighResolutionGate();

        Assert.True(gate.TryResumeCaptureAfterHighResolutionFailure());

        Assert.Equal(CaptureSessionState.Capturing, gate.State);
        Assert.False(gate.TryBeginCapture());
        gate.EndCapture();
        Assert.Equal(CaptureSessionState.Idle, gate.State);
    }

    [Fact]
    public void AutomaticFallbackEndsDirectlyFromHighResolutionTransition()
    {
        var gate = HighResolutionGate();

        gate.EndCapture();

        Assert.Equal(CaptureSessionState.Idle, gate.State);
        Assert.True(gate.TryBeginCapture());
    }

    [Fact]
    public void ResumeIsRejectedOutsideHighResolutionTransition()
    {
        var gate = new CaptureSessionGate();

        Assert.False(gate.TryResumeCaptureAfterHighResolutionFailure());
        Assert.True(gate.TryBeginCapture());
        Assert.False(gate.TryResumeCaptureAfterHighResolutionFailure());
    }

    [Fact]
    public void ShutdownIsTerminalFromActiveHighResolutionTransition()
    {
        var gate = HighResolutionGate();

        gate.Shutdown();
        gate.EndCapture();

        Assert.True(gate.IsShutdown);
        Assert.False(gate.TryBeginCapture());
        Assert.False(gate.TryBeginHighResolutionTransition());
        Assert.False(gate.TryResumeCaptureAfterHighResolutionFailure());
    }

    [Fact]
    public async Task ConcurrentBeginAllowsExactlyOneCapture()
    {
        var gate = new CaptureSessionGate();
        using var ready = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                ready.Wait();
                return gate.TryBeginCapture();
            }))
            .ToArray();

        ready.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, result => result);
        Assert.Equal(CaptureSessionState.Capturing, gate.State);
    }

    private static CaptureSessionGate HighResolutionGate()
    {
        var gate = new CaptureSessionGate();
        Assert.True(gate.TryBeginCapture());
        Assert.True(gate.TryBeginHighResolutionTransition());
        return gate;
    }
}
