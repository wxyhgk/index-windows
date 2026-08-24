using System.Diagnostics;
using Index.Platform.Export;
using Index.Storage;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Index.UI.Gallery;

/// <summary>图库单张动作；收口数据库、文件系统、剪贴板和 Shell 副作用。</summary>
internal sealed class GalleryShotCommands
{
    private readonly ShotStore _store;
    private readonly LibraryOrganizationStore _organization;
    private readonly IImageExporter _exporter;
    private readonly DispatcherQueue _dispatcher;

    public GalleryShotCommands(
        ShotStore store,
        LibraryOrganizationStore organization,
        DispatcherQueue dispatcher)
    {
        _store = store;
        _organization = organization;
        _dispatcher = dispatcher;
        _exporter = new WindowsImageExporter();
    }

    public async Task CopyAsync(ShotRecord shot, CancellationToken cancellationToken = default)
    {
        var file = await StorageFile.GetFileFromPathAsync(_store.OriginalPath(shot));
        cancellationToken.ThrowIfCancellationRequested();
        await RunOnUiAsync(() =>
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        });
    }

    public async Task<string> ExportAsync(ShotRecord shot, CancellationToken cancellationToken = default)
    {
        var png = await File.ReadAllBytesAsync(_store.OriginalPath(shot), cancellationToken);
        var name = $"Index_{shot.CapturedAt.ToLocalTime():yyyyMMdd_HHmmss}";
        var result = await _exporter.ExportPngAsync(png, name, cancellationToken);
        return result.Path;
    }

    public void OpenOriginal(ShotRecord shot)
    {
        Process.Start(new ProcessStartInfo(_store.OriginalPath(shot))
        {
            UseShellExecute = true
        });
    }

    public void OpenSource(ShotRecord shot)
    {
        if (!Uri.TryCreate(shot.SourceUrl, UriKind.Absolute, out var source)
            || source.Scheme is not ("http" or "https"))
            return;
        Process.Start(new ProcessStartInfo(source.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    public Task<bool> DeleteAsync(ShotRecord shot, CancellationToken cancellationToken = default)
        => _store.DeleteAsync(shot.Id, cancellationToken);

    public async Task<bool> IsFavoriteAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
        => (await _organization.GetFavoriteIdsAsync(cancellationToken)).Contains(shot.Id);

    public Task SetFavoriteAsync(
        ShotRecord shot,
        bool isFavorite,
        CancellationToken cancellationToken = default)
        => _organization.SetFavoriteAsync(shot.Id, isFavorite, cancellationToken);

    public Task<IReadOnlyList<string>> GetTagsAsync(
        ShotRecord shot,
        CancellationToken cancellationToken = default)
        => _organization.GetTagsAsync(shot.Id, cancellationToken);

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("主窗口已经关闭。"));
        }
        return completion.Task;
    }
}
