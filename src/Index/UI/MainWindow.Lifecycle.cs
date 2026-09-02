using Index.Platform;
using Index.Storage;
using Index.UI.Applications;
using Index.UI.Gallery;
using Microsoft.UI.Xaml;

namespace Index.UI;

public sealed partial class MainWindow
{
    private void OnCaptureSaved(StoredCapture capture)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosing)
                return;
            if (_navigation.IsAppsWorkspace)
            {
                if (_previewView is not null
                    && _contentHost.IsOverlayVisible(_previewView))
                {
                    _applicationsRefreshPending = true;
                    return;
                }
                _pageOwner.Find<ApplicationsWorkspaceView>()?.Refresh();
                return;
            }
            if (!_navigation.IsLibraryShots)
                return;
            if (_previewView is not null
                && _contentHost.IsOverlayVisible(_previewView))
            {
                _galleryRefreshPending = true;
                return;
            }
            var generation = _navigation.Refresh();
            _ = LoadShotGalleryAsync(generation);
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _isClosing = true;
        _galleryRefreshPending = false;
        _applicationsRefreshPending = false;
        RunCloseStep(() => _navigation.Refresh());
        RunCloseStep(() => _ = _coordinator.BeginShutdown());
        RunCloseStep(CancelTrayMemoryTrim);
        RunCloseStep(() => ClosePreview(refreshSource: false));
        RunCloseStep(_pageOwner.Dispose);
        RunCloseStep(_libraryWorkspaceController.Dispose);
        RunCloseStep(() => _previewView?.Dispose());
        _previewView = null;
        RunCloseStep(() => _shotLibrary.CaptureSaved -= OnCaptureSaved);
        RunCloseStep(() => AppWindow.Closing -= OnAppWindowClosing);
        RunCloseStep(() => Closed -= OnClosed);
    }

    private static void RunCloseStep(Action step)
    {
        try
        {
            step();
        }
        catch (Exception error)
        {
            // Closed event subscribers run sequentially. Never prevent the composition root's
            // later handler from canceling and awaiting display/window lease restoration.
            System.Diagnostics.Debug.WriteLine($"Main window cleanup failed: {error}");
        }
    }

    private void OnAppWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (!_closeToTrayEnabled || _exitRequested)
            return;

        args.Cancel = true;
        sender.Hide();
        _pageOwner.Find<ShotGalleryGridView>()?.SetBackgrounded(true);
        ScheduleTrayMemoryTrim();
    }

    public void EnableCloseToTray() => _closeToTrayEnabled = true;

    public void ShowFromTray()
    {
        CancelTrayMemoryTrim();
        _pageOwner.Find<ShotGalleryGridView>()?.SetBackgrounded(false);
        AppWindow.Show();
        Activate();
    }

    private void ScheduleTrayMemoryTrim()
    {
        CancelTrayMemoryTrim();
        var cancellation = new CancellationTokenSource();
        _trayTrimCancellation = cancellation;
        _ = TrimTrayMemoryAsync(cancellation);
    }

    private async Task TrimTrayMemoryAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token)
                .ConfigureAwait(false);
            ThumbnailLoader.Shared.Clear();
            WebsiteFaviconProvider.Shared.Clear();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_trayTrimCancellation, cancellation))
                _trayTrimCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelTrayMemoryTrim()
    {
        var cancellation = _trayTrimCancellation;
        _trayTrimCancellation = null;
        cancellation?.Cancel();
    }

    public void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }
}
