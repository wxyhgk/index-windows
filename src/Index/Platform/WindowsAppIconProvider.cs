using System.Drawing;
using System.Drawing.Imaging;

namespace Index.Platform;

/// <summary>
/// exe 路径到应用图标 PNG 的后台 LRU 缓存。同一路径的并发请求只提取一次，
/// 避免图库首屏在 UI 线程逐卡读取版本资源。
/// </summary>
public sealed class WindowsAppIconProvider
{
    private sealed record CacheItem(byte[] Png, LinkedListNode<string> Node);

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheItem> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<byte[]?>> _inflight =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recency = new();
    private readonly int _capacity;

    public static WindowsAppIconProvider Shared { get; } = new();

    public WindowsAppIconProvider(int capacity = 128)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public async Task<byte[]?> GetIconPngAsync(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        string path;
        try { path = Path.GetFullPath(executablePath); }
        catch { return null; }
        if (!File.Exists(path)) return null;

        Task<byte[]?> task;
        lock (_gate)
        {
            if (_cache.TryGetValue(path, out var hit))
            {
                _recency.Remove(hit.Node);
                _recency.AddFirst(hit.Node);
                return hit.Png;
            }

            if (!_inflight.TryGetValue(path, out task!))
            {
                task = Task.Run(() => ExtractIcon(path));
                _inflight.Add(path, task);
            }
        }

        var png = await task.ConfigureAwait(false);
        lock (_gate)
        {
            _inflight.Remove(path);
            if (png is null || _cache.ContainsKey(path)) return png;

            var node = _recency.AddFirst(path);
            _cache.Add(path, new CacheItem(png, node));
            while (_cache.Count > _capacity && _recency.Last is { } tail)
            {
                _recency.RemoveLast();
                _cache.Remove(tail.Value);
            }
        }
        return png;
    }

    private static byte[]? ExtractIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
