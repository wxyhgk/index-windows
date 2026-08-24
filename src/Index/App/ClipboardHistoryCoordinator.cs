using Index.Clipboard;

namespace Index.App;

/// <summary>平台监听 → 快照解析 → 历史仓储的单向协调器。</summary>
public sealed class ClipboardHistoryCoordinator : IDisposable
{
    private readonly IClipboardChangeWatcher _watcher;
    private readonly IClipboardSnapshotReader _reader;
    private readonly IClipboardHistoryStore _store;
    private readonly ClipboardReplaySuppression? _replaySuppression;
    private int _reading;
    private int _pending;
    private bool _started;

    public ClipboardHistoryCoordinator(
        IClipboardChangeWatcher watcher,
        IClipboardSnapshotReader reader,
        IClipboardHistoryStore store,
        ClipboardReplaySuppression? replaySuppression = null)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _replaySuppression = replaySuppression;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _watcher.Changed += OnClipboardChanged;
        _watcher.Start();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _watcher.Stop();
        _watcher.Changed -= OnClipboardChanged;
    }

    private async void OnClipboardChanged()
    {
        Interlocked.Exchange(ref _pending, 1);
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0) return;
        try
        {
            do
            {
                Interlocked.Exchange(ref _pending, 0);
                var snapshot = await _reader.ReadAsync();
                if (snapshot is not null
                    && !(_replaySuppression?.ShouldSuppress(snapshot.ContentHash) ?? false))
                    await _store.RecordAsync(snapshot);
            }
            while (Volatile.Read(ref _pending) != 0);
        }
        catch (Exception error)
        {
            TryLog(error);
        }
        finally
        {
            Interlocked.Exchange(ref _reading, 0);
            // reading 归零的瞬间若又来了变化，重新取得执行权，不能吞掉最后一条。
            if (Interlocked.Exchange(ref _pending, 0) != 0)
                OnClipboardChanged();
        }
    }

    private static void TryLog(Exception error)
    {
        try
        {
            File.AppendAllText(
                @"C:\temp\index_clipboard.log",
                $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}");
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _watcher.Dispose();
    }
}
