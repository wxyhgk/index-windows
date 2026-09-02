namespace Index.Navigation;

public sealed record MainNavigationDestination(
    string Id,
    MainNavigationPage Page,
    string Title,
    int Order);

public sealed class MainNavigationDestinationRegistry
{
    private static readonly MainNavigationPage[] RequiredTopLevelPages =
    [
        MainNavigationPage.Library,
        MainNavigationPage.Collections,
        MainNavigationPage.Apps,
        MainNavigationPage.Settings
    ];

    private readonly IReadOnlyList<MainNavigationDestination> _destinations;
    private readonly IReadOnlyDictionary<MainNavigationPage, MainNavigationDestination> _byPage;

    public MainNavigationDestinationRegistry(IEnumerable<MainNavigationDestination> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        var registered = destinations.ToArray();
        if (registered.Any(destination => destination is null))
            throw new ArgumentException("Destinations cannot contain null entries.", nameof(destinations));
        if (registered.Any(destination => string.IsNullOrWhiteSpace(destination.Id)))
            throw new ArgumentException("Destination IDs cannot be empty.", nameof(destinations));

        var duplicateId = registered
            .GroupBy(destination => destination.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Destination ID '{duplicateId.Key}' is registered more than once.",
                nameof(destinations));
        }

        var duplicatePage = registered
            .GroupBy(destination => destination.Page)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePage is not null)
        {
            throw new ArgumentException(
                $"Page '{duplicatePage.Key}' is registered more than once.",
                nameof(destinations));
        }

        var missingPages = RequiredTopLevelPages.Except(registered.Select(destination => destination.Page)).ToArray();
        var unexpectedPages = registered.Select(destination => destination.Page).Except(RequiredTopLevelPages).ToArray();
        if (missingPages.Length > 0 || unexpectedPages.Length > 0)
        {
            throw new ArgumentException(
                $"Top-level destinations must exactly register: {string.Join(", ", RequiredTopLevelPages)}.",
                nameof(destinations));
        }

        _destinations = Array.AsReadOnly(registered
            .OrderBy(destination => destination.Order)
            .ThenBy(destination => destination.Id, StringComparer.Ordinal)
            .ToArray());
        _byPage = registered.ToDictionary(destination => destination.Page);
    }

    public IReadOnlyList<MainNavigationDestination> Destinations => _destinations;

    public MainNavigationDestination Get(MainNavigationPage page) =>
        _byPage.TryGetValue(page, out var destination)
            ? destination
            : throw new KeyNotFoundException($"Page '{page}' is not a top-level destination.");

    public static MainNavigationDestinationRegistry CreateDefault() => new(
    [
        new MainNavigationDestination("library", MainNavigationPage.Library, "内容库", 0),
        new MainNavigationDestination("collections", MainNavigationPage.Collections, "收藏", 100),
        new MainNavigationDestination("apps", MainNavigationPage.Apps, "应用", 200),
        new MainNavigationDestination("settings", MainNavigationPage.Settings, "设置", 300)
    ]);
}
