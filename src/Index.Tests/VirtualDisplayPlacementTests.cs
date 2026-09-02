using Index.Platform;

namespace Index.Tests;

public sealed class VirtualDisplayPlacementTests
{
    [Fact]
    public void PlacesVirtualDisplayRightOfTheWholeDesktop()
    {
        var placement = VirtualDisplayPlacement.FindRightOf(
        [
            new DisplayBounds(-1920, 200, 1920, 1080),
            new DisplayBounds(0, 0, 2560, 1440),
            new DisplayBounds(0, 1440, 1920, 1200)
        ]);

        Assert.Equal((2560, 0), placement);
    }

    [Fact]
    public void UsesOriginWhenThereAreNoActiveDisplays()
    {
        Assert.Equal((0, 0), VirtualDisplayPlacement.FindRightOf([]));
    }
}
