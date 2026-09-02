using Index.Platform;

namespace Index.Tests;

public sealed class MttVirtualDisplayIdentityTests
{
    [Theory]
    [InlineData(@"MONITOR\MTT1337\{GUID}\0001", "Generic PnP Monitor")]
    [InlineData(@"DISPLAY\MTT1337\1&ABC&0&UID256", "Generic Monitor")]
    [InlineData(@"MONITOR\UNKNOWN\0001", "Generic Monitor (VDD by MTT)")]
    public void AcceptsMttEdidOrExplicitFriendlyName(string displayId, string deviceName)
    {
        Assert.True(MttVirtualDisplayIdentity.IsMatch(displayId, deviceName));
    }

    [Theory]
    [InlineData(@"ROOT\DISPLAY\0000", "Parsec Virtual Display Adapter")]
    [InlineData(@"ROOT\DISPLAY\0001", "Todesk Virtual Display Adapter")]
    [InlineData(@"MONITOR\DELF146\0000", "Dell E2423H")]
    public void RejectsOtherPhysicalAndVirtualDisplays(string displayId, string deviceName)
    {
        Assert.False(MttVirtualDisplayIdentity.IsMatch(displayId, deviceName));
    }
}
