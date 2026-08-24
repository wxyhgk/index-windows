using Index.Capture;

namespace Index.Tests;

public sealed class SelectionOverlaySessionStateTests
{
    [Fact]
    public void Activate_MakesOneDisplayActiveAndReturnsAllOthers()
    {
        var state = new SelectionOverlaySessionState(["display-2", "display-0", "display-1"]);

        var inactive = state.Activate("display-1");

        Assert.Equal("display-1", state.ActiveDisplayId);
        Assert.Equal(["display-0", "display-2"], inactive);
        Assert.Equal(SelectionOverlaySessionStatus.Active, state.Status);
    }

    [Fact]
    public void CompletionGate_AcceptsExactlyOneDisplay()
    {
        var state = new SelectionOverlaySessionState(["display-0", "display-1"]);

        Assert.True(state.TryComplete("display-1"));
        Assert.False(state.TryComplete("display-0"));
        Assert.False(state.TryCancel());
        Assert.Equal(SelectionOverlaySessionStatus.Completed, state.Status);
        Assert.Equal("display-1", state.ActiveDisplayId);
    }

    [Fact]
    public void CancelGate_PreventsLateCompletion()
    {
        var state = new SelectionOverlaySessionState(["display-0", "display-1"]);

        Assert.True(state.TryCancel());
        Assert.False(state.TryCancel());
        Assert.False(state.TryComplete("display-0"));
        Assert.Equal(SelectionOverlaySessionStatus.Canceled, state.Status);
    }

    [Fact]
    public void UnknownDisplay_IsRejectedBeforeChangingState()
    {
        var state = new SelectionOverlaySessionState(["display-0", "display-1"]);

        Assert.Throws<ArgumentException>(() => state.Activate("display-3"));
        Assert.Equal(SelectionOverlaySessionStatus.Active, state.Status);
        Assert.Null(state.ActiveDisplayId);
    }
}
