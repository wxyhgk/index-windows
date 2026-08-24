using System.Runtime.InteropServices;
using Index.Clipboard;
using Microsoft.UI.Dispatching;

namespace Index.Platform.Clipboard;

/// <summary>使用系统剪切板序列号轮询变化，不读取内容、不依赖存储层。</summary>
public sealed class WindowsClipboardWatcher : IClipboardChangeWatcher
{
    private readonly DispatcherQueueTimer _timer;
    private uint _lastSequence;
    private bool _running;

    public WindowsClipboardWatcher(DispatcherQueue? dispatcher = null)
    {
        var queue = dispatcher
            ?? DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("剪切板监听器必须在 UI 线程创建。");
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    public event Action? Changed;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastSequence = GetClipboardSequenceNumber();
        _timer.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var sequence = GetClipboardSequenceNumber();
        if (sequence == 0 || sequence == _lastSequence) return;
        _lastSequence = sequence;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
