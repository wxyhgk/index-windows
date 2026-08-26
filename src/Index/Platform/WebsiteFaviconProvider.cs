namespace Index.Platform;

/// <summary>按站点 origin 缓存 favicon；失败返回 null，由 UI 回退到来源应用图标。</summary>
public sealed class WebsiteFaviconProvider
{
    private const int MaximumBytes = 512 * 1024;
    private const int MaximumCacheBytes = 8 * 1024 * 1024;
    private const int MaximumEntries = 128;
    private static readonly TimeSpan FailureTimeToLive = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _cacheBytes;

    public static WebsiteFaviconProvider Shared { get; } = new();

    public async Task<byte[]?> GetAsync(string? pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)
            || page.Scheme is not ("http" or "https"))
            return null;

        var origin = page.GetLeftPart(UriPartial.Authority);
        Lazy<Task<byte[]?>> pending;
        lock (_cacheGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_cache.TryGetValue(origin, out var cached))
            {
                if (cached.ResultKnown
                    && cached.Bytes == 0
                    && now - cached.CreatedAt >= FailureTimeToLive)
                {
                    RemoveEntry(origin, cached);
                }
                else
                {
                    cached.LastAccess = now;
                    pending = cached.Download;
                    goto AwaitResult;
                }
            }

            pending = new Lazy<Task<byte[]?>>(
                () => DownloadAsync(new Uri(new Uri(origin), "/favicon.ico")),
                LazyThreadSafetyMode.ExecutionAndPublication);
            _cache[origin] = new CacheEntry(pending, now);
        }

    AwaitResult:
        var result = await pending.Value;
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(origin, out var current)
                && ReferenceEquals(current.Download, pending)
                && !current.ResultKnown)
            {
                current.ResultKnown = true;
                current.Bytes = result?.LongLength ?? 0;
                _cacheBytes += current.Bytes;
                TrimCache();
            }
        }
        return result;
    }

    public void Clear()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _cacheBytes = 0;
        }
    }

    private void TrimCache()
    {
        while (_cache.Count > MaximumEntries || _cacheBytes > MaximumCacheBytes)
        {
            var oldest = _cache
                .Where(pair => pair.Value.ResultKnown)
                .OrderBy(pair => pair.Value.LastAccess)
                .FirstOrDefault();
            if (oldest.Value is null)
                return;
            RemoveEntry(oldest.Key, oldest.Value);
        }
    }

    private void RemoveEntry(string key, CacheEntry entry)
    {
        if (_cache.Remove(key))
            _cacheBytes -= entry.Bytes;
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

    private sealed class CacheEntry(
        Lazy<Task<byte[]?>> download,
        DateTimeOffset createdAt)
    {
        public Lazy<Task<byte[]?>> Download { get; } = download;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset LastAccess { get; set; } = createdAt;
        public long Bytes { get; set; }
        public bool ResultKnown { get; set; }
    }
}
