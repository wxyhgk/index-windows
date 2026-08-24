using SkiaSharp;

namespace Index.UI.Gallery;

/// <summary>
/// 缩略图的一份借用。调用方拥有 lease 并必须 Dispose；不得自行 Dispose Bitmap。
/// lease 存活期间，即使对应缓存项已被淘汰，Bitmap 也不会被释放。
/// </summary>
public sealed class ThumbnailLease : IDisposable
{
    private ThumbnailCacheEntry? _entry;

    internal ThumbnailLease(ThumbnailCacheEntry entry)
    {
        _entry = entry;
    }

    public SKBitmap Bitmap
        => Volatile.Read(ref _entry)?.Bitmap
           ?? throw new ObjectDisposedException(nameof(ThumbnailLease));

    public int PixelWidth => Bitmap.Width;
    public int PixelHeight => Bitmap.Height;

    public void Dispose()
    {
        Interlocked.Exchange(ref _entry, null)?.ReleaseReference();
    }
}

/// <summary>缓存持有所有权，lease 持有引用；两者都释放后才销毁 Skia 对象。</summary>
internal sealed class ThumbnailCacheEntry
{
    private readonly object _gate = new();
    private SKBitmap? _bitmap;
    private int _references;
    private bool _cacheOwned;

    public ThumbnailCacheEntry(SKBitmap bitmap)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        // 创建者（in-flight load）先持有一份引用，保护等待者取得各自 lease。
        _references = 1;
        EstimatedBytes = checked((long)bitmap.RowBytes * bitmap.Height);
    }

    public long EstimatedBytes { get; }

    public SKBitmap Bitmap
    {
        get
        {
            lock (_gate)
                return _bitmap ?? throw new ObjectDisposedException(nameof(ThumbnailCacheEntry));
        }
    }

    public ThumbnailLease AcquireLease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_bitmap is null, this);
            _references++;
            return new ThumbnailLease(this);
        }
    }

    public void AddCacheOwnership()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_bitmap is null, this);
            if (_cacheOwned)
                throw new InvalidOperationException("缩略图已经属于缓存。");
            _cacheOwned = true;
        }
    }

    public void RemoveCacheOwnership()
    {
        SKBitmap? dispose = null;
        lock (_gate)
        {
            if (!_cacheOwned) return;
            _cacheOwned = false;
            if (_references == 0)
            {
                dispose = _bitmap;
                _bitmap = null;
            }
        }
        dispose?.Dispose();
    }

    public void ReleaseReference()
    {
        SKBitmap? dispose = null;
        lock (_gate)
        {
            if (_references <= 0)
                throw new InvalidOperationException("缩略图引用计数失衡。");
            _references--;
            if (_references == 0 && !_cacheOwned)
            {
                dispose = _bitmap;
                _bitmap = null;
            }
        }
        dispose?.Dispose();
    }
}
