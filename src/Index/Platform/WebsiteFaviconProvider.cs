using System.Collections.Concurrent;

namespace Index.Platform;

/// <summary>按站点 origin 缓存 favicon；失败返回 null，由 UI 回退到来源应用图标。</summary>
public sealed class WebsiteFaviconProvider
{
    private const int MaximumBytes = 512 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _cache = new();

    public static WebsiteFaviconProvider Shared { get; } = new();

    public Task<byte[]?> GetAsync(string? pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)
            || page.Scheme is not ("http" or "https"))
            return Task.FromResult<byte[]?>(null);

        var origin = page.GetLeftPart(UriPartial.Authority);
        return _cache.GetOrAdd(
            origin,
            key => new Lazy<Task<byte[]?>>(
                () => DownloadAsync(new Uri(new Uri(key), "/favicon.ico")),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static async Task<byte[]?> DownloadAsync(Uri favicon)
    {
        try
        {
            using var response = await Client.GetAsync(
                favicon, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength > MaximumBytes)
                return null;

            await using var input = await response.Content.ReadAsStreamAsync();
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer);
                if (read == 0) break;
                if (output.Length + read > MaximumBytes) return null;
                output.Write(buffer, 0, read);
            }
            return output.Length == 0 ? null : output.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
