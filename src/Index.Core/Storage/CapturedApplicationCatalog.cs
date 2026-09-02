namespace Index.Storage;

public enum CapturedApplicationSort
{
    CaptureCount,
    Recent,
    Name
}

public sealed record CapturedApplicationOverview(
    IReadOnlyList<CapturedApplicationSummary> Recent,
    IReadOnlyList<CapturedApplicationSummary> Frequent,
    IReadOnlyList<CapturedApplicationSummary> All);

/// <summary>Pure filtering and sectioning rules for the applications workspace.</summary>
public static class CapturedApplicationCatalog
{
    public static CapturedApplicationOverview Create(
        IReadOnlyList<CapturedApplicationSummary> applications,
        string? searchText,
        CapturedApplicationSort sort,
        int recentLimit = 5,
        int frequentLimit = 7)
    {
        ArgumentNullException.ThrowIfNull(applications);
        if (recentLimit < 0) throw new ArgumentOutOfRangeException(nameof(recentLimit));
        if (frequentLimit < 0) throw new ArgumentOutOfRangeException(nameof(frequentLimit));

        var query = searchText?.Trim();
        var matching = string.IsNullOrEmpty(query)
            ? applications.ToArray()
            : applications.Where(application =>
                    application.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || (application.AppIdentifier?.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();

        var all = Sort(matching, sort);
        if (!string.IsNullOrEmpty(query))
            return new CapturedApplicationOverview([], [], all);

        var recent = matching
            .OrderByDescending(application => application.LastCapturedAt)
            .ThenByDescending(application => application.CaptureCount)
            .ThenBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(recentLimit)
            .ToArray();
        var recentIds = recent
            .Select(application => application.StableId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var frequent = matching
            .Where(application => !recentIds.Contains(application.StableId))
            .OrderByDescending(application => application.CaptureCount)
            .ThenByDescending(application => application.LastCapturedAt)
            .ThenBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(frequentLimit)
            .ToArray();

        return new CapturedApplicationOverview(recent, frequent, all);
    }

    private static CapturedApplicationSummary[] Sort(
        IEnumerable<CapturedApplicationSummary> applications,
        CapturedApplicationSort sort) => sort switch
    {
        CapturedApplicationSort.Recent => applications
            .OrderByDescending(application => application.LastCapturedAt)
            .ThenByDescending(application => application.CaptureCount)
            .ThenBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray(),
        CapturedApplicationSort.Name => applications
            .OrderBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(application => application.CaptureCount)
            .ToArray(),
        _ => applications
            .OrderByDescending(application => application.CaptureCount)
            .ThenByDescending(application => application.LastCapturedAt)
            .ThenBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray()
    };
}
