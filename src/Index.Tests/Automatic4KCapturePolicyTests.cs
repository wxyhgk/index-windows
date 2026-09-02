using Index.Actions;
using Index.Capture;

namespace Index.Tests;

public sealed class Automatic4KCapturePolicyTests
{
    [Theory]
    [InlineData(CaptureActionIds.Complete)]
    [InlineData(CaptureActionIds.Copy)]
    public void UpgradesWindowOutputActions(string actionId)
    {
        Assert.True(Automatic4KCapturePolicy.ShouldUpgrade(true, actionId, (nint)123));
    }

    [Fact]
    public void DoesNotUpgradeArbitraryDesktopSelection()
    {
        Assert.False(Automatic4KCapturePolicy.ShouldUpgrade(
            true,
            CaptureActionIds.Complete,
            nint.Zero));
    }

    [Fact]
    public void DoesNotUpgradePinOrDisabledMode()
    {
        Assert.False(Automatic4KCapturePolicy.ShouldUpgrade(
            true,
            CaptureActionIds.Pin,
            (nint)123));
        Assert.False(Automatic4KCapturePolicy.ShouldUpgrade(
            false,
            CaptureActionIds.Complete,
            (nint)123));
    }
}
