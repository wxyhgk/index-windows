using Index.Navigation;

namespace Index.Tests;

public sealed class MainNavigationDestinationRegistryTests
{
    [Fact]
    public void DefaultDestinationsHaveUniqueStableIds()
    {
        var destinations = MainNavigationDestinationRegistry.CreateDefault().Destinations;

        Assert.Equal(
            destinations.Count,
            destinations.Select(destination => destination.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(destinations, destination => Assert.False(string.IsNullOrWhiteSpace(destination.Id)));
    }

    [Fact]
    public void DefaultRegistryContainsEveryTopLevelPageExactlyOnce()
    {
        var pages = MainNavigationDestinationRegistry.CreateDefault()
            .Destinations
            .Select(destination => destination.Page)
            .ToArray();

        Assert.Equal(
            [
                MainNavigationPage.Library,
                MainNavigationPage.Collections,
                MainNavigationPage.Apps,
                MainNavigationPage.Settings
            ],
            pages);
    }

    [Fact]
    public void RegistryUsesOrderThenIdForDeterministicOrdering()
    {
        var registry = new MainNavigationDestinationRegistry(
        [
            new MainNavigationDestination("settings", MainNavigationPage.Settings, "Settings", 20),
            new MainNavigationDestination("library", MainNavigationPage.Library, "Library", 0),
            new MainNavigationDestination("collections", MainNavigationPage.Collections, "Collections", 10),
            new MainNavigationDestination("apps", MainNavigationPage.Apps, "Apps", 10)
        ]);

        Assert.Equal(
            ["library", "apps", "collections", "settings"],
            registry.Destinations.Select(destination => destination.Id));
    }

    [Fact]
    public void RegistryRejectsDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => new MainNavigationDestinationRegistry(
        [
            new MainNavigationDestination("same", MainNavigationPage.Library, "Library", 0),
            new MainNavigationDestination("same", MainNavigationPage.Collections, "Collections", 10),
            new MainNavigationDestination("apps", MainNavigationPage.Apps, "Apps", 20),
            new MainNavigationDestination("settings", MainNavigationPage.Settings, "Settings", 30)
        ]));
    }

    [Fact]
    public void RegistryRejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => new MainNavigationDestinationRegistry(
        [
            new MainNavigationDestination("", MainNavigationPage.Library, "Library", 0),
            new MainNavigationDestination("collections", MainNavigationPage.Collections, "Collections", 10),
            new MainNavigationDestination("apps", MainNavigationPage.Apps, "Apps", 20),
            new MainNavigationDestination("settings", MainNavigationPage.Settings, "Settings", 30)
        ]));
    }

    [Fact]
    public void RegistryRejectsIncompleteRegistration()
    {
        Assert.Throws<ArgumentException>(() => new MainNavigationDestinationRegistry(
        [
            new MainNavigationDestination("library", MainNavigationPage.Library, "Library", 0),
            new MainNavigationDestination("collections", MainNavigationPage.Collections, "Collections", 10),
            new MainNavigationDestination("apps", MainNavigationPage.Apps, "Apps", 20)
        ]));
    }
}
