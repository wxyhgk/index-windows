using Index.Platform;
using Microsoft.Data.Sqlite;

namespace Index.Tests;

public sealed class WindowsBrowserSourceMetadataResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"index-browser-metadata-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Index - Google Chrome", "chrome", "Index")]
    [InlineData("Docs - Microsoft Edge", "msedge", "Docs")]
    [InlineData("News — Mozilla Firefox", "firefox", "News")]
    [InlineData("Index 和另外 4 个页面 - 个人 - Microsoft​ Edge", "msedge", "Index")]
    [InlineData("Editor", "code", null)]
    public void NormalizePageTitle_RecognizesSupportedBrowsers(
        string title, string processName, string? expected)
    {
        Assert.Equal(
            expected,
            WindowsBrowserSourceMetadataResolver.NormalizePageTitle(title, processName));
    }

    [Fact]
    public void ChromiumHistory_UsesExactWindowTitleAndReturnsWebUrl()
    {
        Directory.CreateDirectory(_root);
        var history = Path.Combine(_root, "History");
        using (var connection = new SqliteConnection($"Data Source={history}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE urls(url TEXT, title TEXT, last_visit_time INTEGER);
                INSERT INTO urls VALUES
                    ('https://wrong.example/', 'Other page', 20),
                    ('https://index.example/docs', 'Index', 10);
                """;
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var result = WindowsBrowserSourceMetadataResolver.QueryCopiedDatabase(
            history, "Index", firefox: false);

        Assert.Equal("https://index.example/docs", result);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
