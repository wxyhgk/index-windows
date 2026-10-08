using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Index.Platform.Clipboard;

public sealed class WindowsClipboardWriter : IClipboardWriter
{
    private readonly DispatcherQueue _dispatcher;

    public WindowsClipboardWriter(DispatcherQueue? dispatcher = null)
    {
        _dispatcher = dispatcher
            ?? DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("WindowsClipboardWriter must be created on the UI thread.");
    }

    public void WriteText(string text)
    {
        void Write()
        {
            var package = new DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }

        if (_dispatcher.HasThreadAccess)
        {
            Write();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try { Write(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
            }))
            throw new InvalidOperationException("The UI dispatcher is shutting down.");
        completion.Task.GetAwaiter().GetResult();
    }

    public async ValueTask WritePngAsync(
        ReadOnlyMemory<byte> pngData,
        CancellationToken cancellationToken = default)
    {
        var bytes = pngData.ToArray();
        await InvokeOnDispatcherAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The stream must outlive SetContent: SetBitmap/SetData defer
            // reads until another process opens the RandomAccessStreamReference,
            // so disposing it here would yield a blank paste.
            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            var package = new DataPackage();
            var pngReference = RandomAccessStreamReference.CreateFromStream(stream);
            package.SetBitmap(pngReference);
            // SetBitmap guarantees broad Windows compatibility, but several
            // chat/browser clients consume that representation through a DIB
            // conversion and then JPEG-encode it. Advertising the registered
            // native PNG clipboard format lets capable clients preserve the
            // exact lossless payload, which is especially visible on white UI.
            package.SetData("PNG", pngReference);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteFilesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var files = new List<StorageFile>();
        foreach (var path in paths.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
        }
        if (files.Count == 0)
            throw new FileNotFoundException("剪贴板历史中的文件已不存在。");

        await InvokeOnDispatcherAsync(() =>
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetStorageItems(files);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return Task.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task InvokeOnDispatcherAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (_dispatcher.HasThreadAccess)
            return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await action();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            }))
            completion.SetException(new InvalidOperationException("The UI dispatcher is shutting down."));
        return completion.Task;
    }
}
