using Index.Navigation;
using Index.Storage;
using Index.UI.Applications;
using Index.UI.Editor;
using Microsoft.UI.Xaml;

namespace Index.UI;

public sealed partial class MainWindow
{
    private void OpenEditor(ShotRecord shot)
        => OpenEditor(shot, _shotLibrary);

    private void OpenEditor(ShotRecord shot, IShotPageSource shotSource)
    {
        if (_isClosing)
            return;

        ClosePreview(refreshSource: false);
        CloseEditor(refreshSource: false);
        _navigation.ShowEditor();

        _editorShotSource = shotSource ?? throw new ArgumentNullException(nameof(shotSource));
        MountEditor(shot, _editorShotSource);
    }

    private void MountEditor(ShotRecord shot, IShotPageSource shotSource)
    {
        var editor = CreateEditorView(shot, shotSource);
        _editorView = editor;
        _contentHost.ShowOverlay(editor);
        editor.Focus(FocusState.Programmatic);
    }

    private ShotEditorWorkspaceView CreateEditorView(
        ShotRecord shot,
        IShotPageSource shotSource)
    {
        var session = _editorSessions.Create(shot);
        try
        {
            var editor = new ShotEditorWorkspaceView(
                session,
                shotSource,
                _shotAssets,
                _galleryCommands,
                _theme);
            editor.CloseRequested += OnEditorCloseRequested;
            editor.ShotSwitchRequested += OnEditorShotSwitchRequested;
            return editor;
        }
        catch
        {
            Task.Run(() => session.DisposeAsync().AsTask())
                .GetAwaiter()
                .GetResult();
            throw;
        }
    }

    private void OnEditorCloseRequested(ShotRecord shot)
        => CloseEditor(refreshSource: true);

    private void OnEditorShotSwitchRequested(ShotRecord shot)
    {
        if (_isClosing || _editorView is null)
            return;

        var previous = _editorView;
        var candidate = CreateEditorView(shot, _editorShotSource ?? _shotLibrary);
        try
        {
            _contentHost.HideOverlay(previous);
            _contentHost.ShowOverlay(candidate);
            _editorView = candidate;
            candidate.Focus(FocusState.Programmatic);
        }
        catch
        {
            candidate.CloseRequested -= OnEditorCloseRequested;
            candidate.ShotSwitchRequested -= OnEditorShotSwitchRequested;
            candidate.Dispose();
            _contentHost.ShowOverlay(previous);
            throw;
        }

        previous.CloseRequested -= OnEditorCloseRequested;
        previous.ShotSwitchRequested -= OnEditorShotSwitchRequested;
        previous.Dispose();
    }

    private void CloseEditor(bool refreshSource)
    {
        var editor = _editorView;
        var wasActive = editor is not null
            && _contentHost.IsOverlayVisible(editor);
        if (editor is not null)
        {
            _contentHost.HideOverlay(editor);
            editor.CloseRequested -= OnEditorCloseRequested;
            editor.ShotSwitchRequested -= OnEditorShotSwitchRequested;
            editor.Dispose();
            _editorView = null;
        }
        _editorShotSource = null;

        if (!wasActive)
            return;

        var returnPage = _navigation.ReturnFromEditor();
        if (_isClosing || !refreshSource)
            return;

        if (returnPage == MainNavigationPage.Library)
        {
            _galleryRefreshPending = false;
            var generation = _navigation.Refresh();
            _ = LoadShotGalleryAsync(generation);
        }
        else if (returnPage == MainNavigationPage.Apps)
        {
            _applicationsRefreshPending = false;
            _pageOwner.Find<ApplicationsWorkspaceView>()?.Refresh();
        }
    }

    private bool IsWorkspaceOverlayVisible()
        => _previewView is not null && _contentHost.IsOverlayVisible(_previewView)
           || _editorView is not null && _contentHost.IsOverlayVisible(_editorView);
}
