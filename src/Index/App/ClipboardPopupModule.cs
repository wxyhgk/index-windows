using Index.Clipboard;
using Index.Platform.Clipboard;
using Index.UI.Clipboard;
using Microsoft.UI.Dispatching;

namespace Index.App;

/// <summary>
/// Composition root for the clipboard popup. Program only needs to retain one instance and call Toggle().
/// </summary>
public sealed class ClipboardPopupModule
{
    private readonly IClipboardHistorySource _source;
    private readonly IClipboardHistoryStore? _store;
    private readonly IClipboardWriter _writer;
    private readonly IClipboardPasteTarget? _pasteTarget;
    private ClipboardPopupWindow? _window;

    public ClipboardPopupModule(
        IClipboardHistorySource source,
        IClipboardWriter writer,
        IClipboardPasteTarget? pasteTarget = null)
    {
        _source = source;
        _store = source as IClipboardHistoryStore;
        _writer = writer;
        _pasteTarget = pasteTarget;
    }

    public static ClipboardPopupModule CreateDemo()
        => new(
            new WindowsClipboardSnapshotSource(),
            new WindowsClipboardWriter(),
            new WindowsClipboardPasteTarget());

    public void Toggle()
    {
        try
        {
            if (_window is not null)
            {
                Close();
                return;
            }

            Show();
        }
        catch (Exception error)
        {
            LogPopupFailure(error);
        }
    }

    public void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _pasteTarget?.RememberForegroundWindow();

        var viewModel = new ClipboardPopupViewModel();
        Func<long, CancellationToken, Task>? togglePinned = _store is null
            ? null
            : (id, cancellationToken) => _store.TogglePinnedAsync(id, cancellationToken);
        Func<long, CancellationToken, Task>? delete = _store is null
            ? null
            : (id, cancellationToken) => _store.DeleteAsync(id, cancellationToken);
        var window = new ClipboardPopupWindow(
            viewModel,
            _writer,
            togglePinned,
            delete,
            PasteAndCloseAsync,
            Close);
        _window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
                _window = null;
        };
        window.Activate();
        window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(_window, window)) return;
            window.InitializeContent();
            window.ApplyWindowChrome();
            _ = LoadAsync(window, viewModel);
        });
    }

    public void Close()
    {
        var window = _window;
        _window = null;
        window?.Close();
    }

    private async Task PasteAndCloseAsync(CancellationToken cancellationToken)
    {
        Close();
        if (_pasteTarget is not null)
            await _pasteTarget.PasteAsync(cancellationToken);
    }

    private async Task LoadAsync(ClipboardPopupWindow window, ClipboardPopupViewModel viewModel)
    {
        try
        {
            var items = await _source.LoadRecentAsync();
            if (!ReferenceEquals(_window, window)) return;
            viewModel.ReplaceItems(items);
            window.Refresh();
        }
        catch (Exception error)
        {
            LogPopupFailure(error);
        }
    }

    private static void LogPopupFailure(Exception error)
    {
        try
        {
            Directory.CreateDirectory(@"C:\temp");
            File.AppendAllText(
                @"C:\temp\index_clipboard_popup_error.log",
                $"[{DateTime.Now:O}] {error}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
