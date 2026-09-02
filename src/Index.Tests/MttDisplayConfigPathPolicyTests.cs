using Index.Platform;

namespace Index.Tests;

public sealed class MttDisplayConfigPathPolicyTests
{
    [Fact]
    public void SelectsOnlyTheAvailableMttTarget()
    {
        DisplayConfigTargetIdentity[] targets =
        [
            new(@"\\?\DISPLAY#DELF146#A", "Dell E2423H", true, true, 0, 1),
            new(@"\\?\DISPLAY#MTT1337#B", "VDD by MTT", false, true, 0, 256),
            new(@"\\?\DISPLAY#RTK1270#C", "Generic Monitor", true, true, 1, 2)
        ];

        Assert.Equal(1, MttDisplayConfigPathPolicy.SelectActivationTarget(targets));
    }

    [Fact]
    public void RejectsAnUnavailableOrUnrelatedTarget()
    {
        DisplayConfigTargetIdentity[] targets =
        [
            new(@"\\?\DISPLAY#MTT1337#A", "VDD by MTT", false, false, 0, 256),
            new(@"\\?\DISPLAY#UNKNOWN#B", "Parsec Virtual Display", false, true, 0, 1)
        ];

        var error = Assert.Throws<InvalidOperationException>(
            () => MttDisplayConfigPathPolicy.SelectActivationTarget(targets));

        Assert.Contains("available DisplayConfig path", error.Message);
    }

    [Fact]
    public void UsesAStableOrderWhenWindowsReturnsMultipleMttSourcePaths()
    {
        DisplayConfigTargetIdentity[] targets =
        [
            new(@"\\?\DISPLAY#MTT1337#A", "VDD by MTT", false, true, 3, 256),
            new(@"\\?\DISPLAY#MTT1337#A", "VDD by MTT", false, true, 0, 256)
        ];

        Assert.Equal(1, MttDisplayConfigPathPolicy.SelectActivationTarget(targets));
    }
}
