using SkiaSharp;

namespace Index.UI.Gallery;

public sealed record ThumbnailLoaderOptions
{
    public int MaxConcurrentLoads { get; init; } = 4;
    public long MaxCacheBytes { get; init; } = 48L * 1024 * 1024;
    public int MaxCacheItems { get; init; } = 160;
    public int MaxPixelDimension { get; init; } = 512;
}

/// <summary>
/// 图库缩略图后台管线：普通文件读取 + Skia 解码、并发限流、同路径请求合并和 LRU 缓存。
/// </summary>
public sealed class ThumbnailLoader : IDisposable
{
    private sealed class CacheItem
    {
        public required ThumbnailCacheEntry Entry { get; init; }
        public required LinkedListNode<string> RecencyNode { get; init; }
    }

    private sealed class InflightLoad
    {
        public required Task<ThumbnailCacheEntry> Task { get; set; }
        public ThumbnailCacheEntry? Entry { get; set; }
        public int WaiterCount { get; set; }
    }

    private readonly object _gate = new();
    private readonly ThumbnailLoaderOptions _options;
    private readonly SemaphoreSlim _loadSlots;
    private readonly Dictionary<string, CacheItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recency = new();
    private readonly Dictionary<string, InflightLoad> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private long _cachedBytes;
    private bool _disposed;

    public static ThumbnailLoader Shared { get; } = new();

    public ThumbnailLoader(ThumbnailLoaderOptions? options = null)
    {
        _options = options ?? new ThumbnailLoaderOptions();
        if (_options.MaxConcurrentLoads <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "最大并发数必须大于零。");
        if (_options.MaxCacheBytes <= 0 || _options.MaxCacheItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "缓存上限必须大于零。");
        if (_options.MaxPixelDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "缩略图边长必须大于零。");
        _loadSlots = new SemaphoreSlim(_options.MaxConcurrentLoads, _options.MaxConcurrentLoads);
    }

    public int CachedItemCount
    {
        get { lock (_gate) return _cache.Count; }
    }

    public long CachedBytes
    {
        get { lock (_gate) return _cachedBytes; }
    }

    /// <summary>
    /// 加载一张缩略图。取消只取消当前调用方的等待，不会破坏同 key 的其他等待者。
    /// </summary>
    public async ValueTask<ThumbnailLease> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string key = NormalizePath(path);
        InflightLoad load;

        lock (_gate)
        {
            ThrowIfDisposed();
            if (TryAcquireCachedLocked(key, out var cached))
                return cached;

            if (!_inflight.TryGetValue(key, out load!))
            {
                load = new InflightLoad { Task = null!, WaiterCount = 0 };
                load.Task = LoadAndPublishAsync(key, load);
                _inflight.Add(key, load);
            }
            load.WaiterCount++;
        }

        try
        {
            var entry = await load.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            // 先取得调用方引用，再释放 in-flight 的保护引用，避免刚发布即淘汰的竞态。
            return entry.AcquireLease();
        }
        finally
        {
            ReleaseWaiter(load);
        }
    }

    /// <summary>依次尝试候选路径，适合“缩略图文件 → 原图文件”的回退。</summary>
    public async ValueTask<ThumbnailLease> LoadFirstAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<Exception>? errors = null;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                return await LoadAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        throw new AggregateException("所有缩略图候选路径都加载失败。", errors ?? []);
    }

    public ValueTask<ThumbnailLease> LoadFirstAsync(
        CancellationToken cancellationToken = default,
        params string[] paths)
        => LoadFirstAsync((IEnumerable<string>)paths, cancellationToken);

    public void Invalidate(string path)
    {
        string key = NormalizePath(path);
        ThumbnailCacheEntry? removed = null;
        lock (_gate)
        {
            if (_cache.Remove(key, out var item))
            {
                _recency.Remove(item.RecencyNode);
                _cachedBytes -= item.Entry.EstimatedBytes;
                removed = item.Entry;
            }
        }
        removed?.RemoveCacheOwnership();
    }

    public void Clear()
    {
        ThumbnailCacheEntry[] removed;
        lock (_gate)
        {
            removed = _cache.Values.Select(item => item.Entry).ToArray();
            _cache.Clear();
            _recency.Clear();
            _cachedBytes = 0;
        }
        foreach (var entry in removed)
            entry.RemoveCacheOwnership();
    }

    private async Task<ThumbnailCacheEntry> LoadAndPublishAsync(string key, InflightLoad load)
    {
        await _loadSlots.WaitAsync().ConfigureAwait(false);
        ThumbnailCacheEntry? entry = null;
        try
        {
            entry = await Task.Run(() => DecodeFile(key, _options.MaxPixelDimension))
                .ConfigureAwait(false);

            bool releaseProducer;
            List<ThumbnailCacheEntry> evicted;
            lock (_gate)
            {
                load.Entry = entry;
                _inflight.Remove(key);
                evicted = _disposed ? [] : AddToCacheLocked(key, entry);
                releaseProducer = load.WaiterCount == 0;
            }

            foreach (var removed in evicted)
                removed.RemoveCacheOwnership();
            if (releaseProducer)
                entry.ReleaseReference();
            return entry;
        }
        catch
        {
            lock (_gate)
                _inflight.Remove(key);
            if (entry is not null)
                entry.ReleaseReference();
            throw;
        }
        finally
        {
            _loadSlots.Release();
        }
    }

    private static ThumbnailCacheEntry DecodeFile(string path, int maxPixelDimension)
    {
        byte[] bytes = File.ReadAllBytes(path);
        SKBitmap? bitmap = SKBitmap.Decode(bytes)
            ?? throw new InvalidDataException($"无法解码缩略图：{Path.GetFileName(path)}");

        try
        {
            int largest = Math.Max(bitmap.Width, bitmap.Height);
            if (largest > maxPixelDimension)
            {
                double scale = maxPixelDimension / (double)largest;
                int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                var resized = bitmap.Resize(
                    new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType),
                    SKSamplingOptions.Default)
                    ?? throw new InvalidDataException($"无法缩放缩略图：{Path.GetFileName(path)}");
                bitmap.Dispose();
                bitmap = resized;
            }

            var entry = new ThumbnailCacheEntry(bitmap);
            bitmap = null;
            return entry;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private List<ThumbnailCacheEntry> AddToCacheLocked(string key, ThumbnailCacheEntry entry)
    {
        var removed = new List<ThumbnailCacheEntry>();
        if (_cache.Remove(key, out var old))
        {
            _recency.Remove(old.RecencyNode);
            _cachedBytes -= old.Entry.EstimatedBytes;
            removed.Add(old.Entry);
        }

        entry.AddCacheOwnership();
        var node = _recency.AddFirst(key);
        _cache.Add(key, new CacheItem { Entry = entry, RecencyNode = node });
        _cachedBytes += entry.EstimatedBytes;

        while ((_cache.Count > _options.MaxCacheItems || _cachedBytes > _options.MaxCacheBytes) &&
               _recency.Last is { } tail)
        {
            string victimKey = tail.Value;
            _recency.RemoveLast();
            var victim = _cache[victimKey];
            _cache.Remove(victimKey);
            _cachedBytes -= victim.Entry.EstimatedBytes;
            removed.Add(victim.Entry);
        }
        return removed;
    }

    private bool TryAcquireCachedLocked(string key, out ThumbnailLease lease)
    {
        if (!_cache.TryGetValue(key, out var item))
        {
            lease = null!;
            return false;
        }

        _recency.Remove(item.RecencyNode);
        _recency.AddFirst(item.RecencyNode);
        lease = item.Entry.AcquireLease();
        return true;
    }

    private void ReleaseWaiter(InflightLoad load)
    {
        ThumbnailCacheEntry? release = null;
        lock (_gate)
        {
            if (load.WaiterCount <= 0)
                throw new InvalidOperationException("缩略图等待者计数失衡。");
            load.WaiterCount--;
            if (load.WaiterCount == 0 && load.Entry is not null)
                release = load.Entry;
        }
        release?.ReleaseReference();
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Clear();
    }
}
