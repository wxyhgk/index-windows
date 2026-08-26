using Index.Clipboard;
using Index.Search;
using Index.Storage;

namespace Index.Tests;

public sealed class UnifiedSearchServiceTests
{
    [Fact]
    public async Task SearchMergesSourcesInTimestampOrderAndAppliesGlobalLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var service = new UnifiedSearchService(
            new FakeShots([
                Shot(1, now.AddMinutes(-2), "older shot"),
                Shot(2, now, "newest shot")
            ]),
            new FakeClipboard([
                new ClipboardHistoryItem(
                    3, ClipboardItemKind.Text, now.AddMinutes(-1), "middle clip",
                    "middle clip", "middle clip", "Terminal")
            ]));

        var results = await service.SearchAsync("query", limit: 2);

        Assert.Equal(["shot-2", "clipboard-3"], results.Select(result => result.Id));
    }

    [Fact]
    public async Task SearchForwardsNormalizedQueryToBothSources()
    {
        var shots = new FakeShots([]);
        var clipboard = new FakeClipboard([]);
        var service = new UnifiedSearchService(shots, clipboard);

        await service.SearchAsync("  微信  ");

        Assert.Equal("微信", shots.Query);
        Assert.Equal("微信", clipboard.Query);
    }

    private static ShotRecord Shot(long id, DateTimeOffset at, string title) => new(
        id, $"sha-{id}", at, 100, 80, 1, "Index", "index", title, null,
        null, null, 0, 0, 100, 80, "png");

    private sealed class FakeShots(IReadOnlyList<ShotRecord> results) : IShotSearchSource
    {
        public string? Query { get; private set; }

        public Task<IReadOnlyList<ShotRecord>> SearchAsync(
            string? query,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult<IReadOnlyList<ShotRecord>>(results.Take(limit).ToArray());
        }
    }

    private sealed class FakeClipboard(IReadOnlyList<ClipboardHistoryItem> results)
        : IClipboardHistorySource
    {
        public string? Query { get; private set; }

        public Task<IReadOnlyList<ClipboardHistoryItem>> LoadRecentAsync(
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClipboardHistoryItem>>(results.Take(limit).ToArray());

        public Task<IReadOnlyList<ClipboardHistoryItem>> SearchAsync(
            string? query,
            ClipboardItemKind? kind,
            int limit,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return LoadRecentAsync(limit, cancellationToken);
        }
    }
}
