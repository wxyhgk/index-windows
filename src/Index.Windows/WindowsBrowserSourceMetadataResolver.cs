using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Index.Platform;

/// <summary>
/// 从浏览器自己的本地历史库匹配当前窗口标题。读取副本避免与浏览器的 WAL/锁竞争，
/// 且只接受标题精确匹配，宁可没有链接也不把另一标签页的网址写进截图。
/// </summary>
public sealed class WindowsBrowserSourceMetadataResolver : IBrowserSourceMetadataResolver
{
    public Task<string?> ResolveUrlAsync(
        SourceApplicationInfo? application,
        CancellationToken cancellationToken = default)
    {
        if (application is null || string.IsNullOrWhiteSpace(application.WindowTitle))
            return Task.FromResult<string?>(null);

        var processName = ProcessName(application.AppIdentifier);
        var title = NormalizePageTitle(application.WindowTitle, processName);
        if (title is null)
            return Task.FromResult<string?>(null);

        return Task.Run(
            () => ResolveFromProfiles(processName, title, cancellationToken),
            cancellationToken);
    }

    internal static string? NormalizePageTitle(string? windowTitle, string? processName)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;
        var title = string.Concat(windowTitle.Trim().Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format));
        var suffixes = (processName ?? "").ToLowerInvariant() switch
        {
            "chrome" => new[] { " - Google Chrome" },
            "msedge" => new[] { " - Microsoft Edge" },
            "brave" => new[] { " - Brave" },
            "firefox" => new[] { " — Mozilla Firefox", " - Mozilla Firefox" },
            "vivaldi" => new[] { " - Vivaldi" },
            "opera" or "opera_gx" => new[] { " - Opera", " - Opera GX" },
            _ => Array.Empty<string>()
        };
        if (suffixes.Length == 0) return null;
        foreach (var suffix in suffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^suffix.Length].Trim();
                break;
            }
        }
        title = Regex.Replace(
            title,
            @"\s+(?:和另外\s*\d+\s*个页面|and\s+\d+\s+other\s+(?:pages?|tabs?))(?:\s+-\s+.*)?$",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string? ResolveFromProfiles(
        string? processName,
        string title,
        CancellationToken cancellationToken)
    {
        foreach (var database in HistoryDatabases(processName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = QueryCopiedDatabase(database, title, processName == "firefox");
            if (IsWebUrl(url)) return url;
        }
        return null;
    }

    internal static string? QueryCopiedDatabase(
        string databasePath,
        string exactTitle,
        bool firefox)
    {
        if (!File.Exists(databasePath)) return null;
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(), "Index", "browser-metadata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var copyPath = Path.Combine(temporaryRoot, Path.GetFileName(databasePath));
        try
        {
            CopyShared(databasePath, copyPath);
            CopyIfPresent(databasePath + "-wal", copyPath + "-wal");
            CopyIfPresent(databasePath + "-shm", copyPath + "-shm");

            using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = copyPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = firefox
                ? """
                    SELECT url FROM moz_places
                    WHERE title = $title OR $title LIKE title || ' - %'
                    ORDER BY CASE WHEN title = $title THEN 0 ELSE 1 END,
                             length(title) DESC, last_visit_date DESC LIMIT 1
                    """
                : """
                    SELECT url FROM urls
                    WHERE title = $title OR $title LIKE title || ' - %'
                    ORDER BY CASE WHEN title = $title THEN 0 ELSE 1 END,
                             length(title) DESC, last_visit_time DESC LIMIT 1
                    """;
            command.Parameters.AddWithValue("$title", exactTitle);
            return command.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, recursive: true); }
            catch { }
        }
    }

    private static IEnumerable<string> HistoryDatabases(string? processName)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return processName switch
        {
            "chrome" => ChromiumProfiles(Path.Combine(local, "Google", "Chrome", "User Data")),
            "msedge" => ChromiumProfiles(Path.Combine(local, "Microsoft", "Edge", "User Data")),
            "brave" => ChromiumProfiles(Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            "vivaldi" => ChromiumProfiles(Path.Combine(local, "Vivaldi", "User Data")),
            "firefox" => FirefoxProfiles(Path.Combine(roaming, "Mozilla", "Firefox", "Profiles")),
            "opera" => Existing(Path.Combine(roaming, "Opera Software", "Opera Stable", "History")),
            "opera_gx" => Existing(Path.Combine(roaming, "Opera Software", "Opera GX Stable", "History")),
            _ => []
        };
    }

    private static IEnumerable<string> ChromiumProfiles(string userDataRoot)
    {
        if (!Directory.Exists(userDataRoot)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(userDataRoot)
                     .Where(path => Path.GetFileName(path) == "Default"
                         || Path.GetFileName(path).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
        {
            var history = Path.Combine(directory, "History");
            if (File.Exists(history)) yield return history;
        }
    }

    private static IEnumerable<string> FirefoxProfiles(string profilesRoot)
    {
        if (!Directory.Exists(profilesRoot)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(profilesRoot))
        {
            var places = Path.Combine(directory, "places.sqlite");
            if (File.Exists(places)) yield return places;
        }
    }

    private static IEnumerable<string> Existing(string path)
    {
        if (File.Exists(path)) yield return path;
    }

    private static void CopyIfPresent(string source, string destination)
    {
        if (File.Exists(source)) CopyShared(source, destination);
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static string? ProcessName(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        return Path.GetFileNameWithoutExtension(identifier.Trim()).ToLowerInvariant();
    }

    private static bool IsWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";
}
