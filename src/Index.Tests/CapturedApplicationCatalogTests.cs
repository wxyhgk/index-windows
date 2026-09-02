using Index.Storage;

namespace Index.Tests;

public sealed class CapturedApplicationCatalogTests
{
    [Fact]
    public void OverviewSeparatesRecentAndFrequentWithoutDuplicatingFeaturedApps()
    {
        var applications = new[]
        {
            App("Edge", "edge.exe", 8, 5),
            App("Explorer", "explorer.exe", 4, 4),
            App("Index", "index.exe", 30, 3),
            App("Terminal", "terminal.exe", 20, 2)
        };

        var overview = CapturedApplicationCatalog.Create(
            applications,
            searchText: null,
            CapturedApplicationSort.CaptureCount,
            recentLimit: 2,
            frequentLimit: 2);

        Assert.Equal(["Edge", "Explorer"], overview.Recent.Select(app => app.Name));
        Assert.Equal(["Index", "Terminal"], overview.Frequent.Select(app => app.Name));
        Assert.Equal(["Index", "Terminal", "Edge", "Explorer"], overview.All.Select(app => app.Name));
    }

    [Fact]
    public void SearchMatchesNameOrIdentifierAndSuppressesFeaturedSections()
    {
        var applications = new[]
        {
            App("Microsoft Edge", @"C:\Apps\msedge.exe", 8, 2),
            App("文件资源管理器", @"C:\Windows\explorer.exe", 4, 1)
        };

        var byName = CapturedApplicationCatalog.Create(
            applications,
            "edge",
            CapturedApplicationSort.Name);
        var byIdentifier = CapturedApplicationCatalog.Create(
            applications,
            "explorer.exe",
            CapturedApplicationSort.Name);

        Assert.Empty(byName.Recent);
        Assert.Empty(byName.Frequent);
        Assert.Equal("Microsoft Edge", Assert.Single(byName.All).Name);
        Assert.Equal("文件资源管理器", Assert.Single(byIdentifier.All).Name);
    }

    private static CapturedApplicationSummary App(
        string name,
        string identifier,
        int count,
        int minute) => new(
        new CapturedApplicationIdentity(name, identifier),
        count,
        new DateTimeOffset(2026, 9, 2, 10, minute, 0, TimeSpan.Zero),
        []);
}
