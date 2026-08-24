using Index.Capture;
using Index.Platform;
using Index.Platform.Capture;

namespace Index.Tests;

public sealed class CoreBoundaryTests
{
    [Fact]
    public void SharedDomainTypes_AreCompiledByCoreAssembly()
    {
        var core = typeof(CaptureArtifact).Assembly;

        Assert.Equal("Index.Core", core.GetName().Name);
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceWindowsUiOrSystemDrawing()
    {
        var references = typeof(CaptureArtifact).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Microsoft.WindowsAppSDK", references);
        Assert.DoesNotContain("Microsoft.WinUI", references);
        Assert.DoesNotContain("System.Drawing.Common", references);
    }

    [Fact]
    public void WindowsInfrastructure_IsCompiledSeparatelyWithoutWinUi()
    {
        var windows = typeof(WindowsCaptureImagePreparer).Assembly;
        var references = windows
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Index.Windows", windows.GetName().Name);
        Assert.Same(windows, typeof(WindowsSourceApplicationResolver).Assembly);
        Assert.Same(windows, typeof(WindowsBrowserSourceMetadataResolver).Assembly);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", references);
        Assert.DoesNotContain("Microsoft.WinUI", references);
    }
}
