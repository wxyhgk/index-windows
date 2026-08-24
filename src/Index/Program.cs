using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Index.App;
using Index.Actions;
using Index.Clipboard;
using Index.Capture;
using Index.Platform;
using Index.Platform.Capture;
using Index.Platform.Clipboard;
using Index.Platform.Export;
using Index.Settings;
using Index.UI;
using Index.UI.Pin;
using Index.Storage;
using Index.Recognition;

namespace Index;

public static class Program
{
    // WinUI Window 与低级键盘钩子都必须由应用生命周期强持有。
    // 只放在 Application.Start 回调的局部变量里会被 GC 回收，原生侧随后访问
    // 已释放 COM 对象，表现为 Microsoft.UI.Xaml/combase 0x80004003 崩溃。
    private static ShotStore? _shotStore;
    private static CaptureCoordinator? _coordinator;
    private static MainWindow? _mainWindow;
    private static ShortcutSettingsStore? _shortcutSettings;
    private static GlobalShortcutController? _shortcutController;
    private static ClipboardPopupModule? _clipboardPopup;
    private static ClipboardHistoryStore? _clipboardStore;
    private static ClipboardHistoryCoordinator? _clipboardHistory;
    private static LibraryOrganizationStore? _libraryOrganization;
    private static PinWindowManager? _pinWindows;
    private static RecognitionPluginRegistry? _recognitionPlugins;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.Start((app) =>
        {
            try
            {
                _shotStore = ShotStore.OpenDefaultAsync().GetAwaiter().GetResult();
                _clipboardStore = ClipboardHistoryStore.OpenDefaultAsync().GetAwaiter().GetResult();
                _libraryOrganization = LibraryOrganizationStore.OpenDefaultAsync().GetAwaiter().GetResult();
                _shortcutSettings = new ShortcutSettingsStore();
                var clipboardWriter = new WindowsClipboardWriter();
                var clipboardReplaySuppression = new ClipboardReplaySuppression();
                var clipboardHistoryWriter = new ClipboardHistoryReplayWriter(
                    clipboardWriter,
                    clipboardReplaySuppression);
                var sourceResolver = new WindowsSourceApplicationResolver();
                _clipboardPopup = new ClipboardPopupModule(
                    _clipboardStore,
                    clipboardHistoryWriter,
                    new WindowsClipboardPasteTarget());
                _clipboardHistory = new ClipboardHistoryCoordinator(
                    new WindowsClipboardWatcher(),
                    new WindowsClipboardSnapshotReader(sourceResolver),
                    _clipboardStore,
                    clipboardReplaySuppression);
                var actionRegistry = new CaptureActionRegistry();
                _pinWindows = new PinWindowManager(actionRegistry);
                actionRegistry.Register(new CompleteCaptureAction());
                actionRegistry.Register(new CopyAction(clipboardWriter));
                actionRegistry.Register(new SaveAction(new WindowsImageExporter()));
                actionRegistry.Register(new CloseCaptureAction());
                actionRegistry.Register(new PinAction(_pinWindows));
                var screenFreezer = new ScreenFreezer();
                var capturePersistence = new CapturePersistenceService(
                    _shotStore,
                    sourceResolver,
                    new WindowsBrowserSourceMetadataResolver());
                _coordinator = new CaptureCoordinator(
                    screenFreezer,
                    screenFreezer,
                    new CaptureActionExecutor(actionRegistry),
                    sourceResolver,
                    new WindowsGraphicsCaptureInterop(),
                    new WindowsCaptureImagePreparer(),
                    capturePersistence,
                    new CaptureActionContextFactory());
                _coordinator.InitOnUiThread();
                var shotAssetReader = new WindowsShotAssetReader(_shotStore);
                _recognitionPlugins = new RecognitionPluginRegistry(
                    new IRecognitionPlugin[]
                    {
                        new MolGrapherClient()
                    });
                _mainWindow = new MainWindow(
                    _coordinator,
                    _shotStore,
                    _libraryOrganization,
                    _shortcutSettings,
                    _clipboardPopup,
                    shotAssetReader,
                    _recognitionPlugins);
                _mainWindow.Activate();
                _shortcutController = new GlobalShortcutController(
                    _shortcutSettings,
                    () => _ = _coordinator.BeginCaptureAsync("hotkey"),
                    () => _mainWindow.DispatcherQueue.TryEnqueue(_mainWindow.ShowLibraryPage),
                    () => _mainWindow.DispatcherQueue.TryEnqueue(_clipboardPopup.Toggle));
                _shortcutController.Start();
                _clipboardHistory.Start();
                _mainWindow.Closed += (_, _) =>
                {
                    _shortcutController?.Dispose();
                    _clipboardHistory?.Dispose();
                    _clipboardPopup?.Close();
                    _pinWindows?.Dispose();
                    _recognitionPlugins?.Dispose();
                };
            }
            catch (Exception error)
            {
                LogStartupFailure(error);
                throw;
            }
        });
    }

    private static void LogStartupFailure(Exception error)
    {
        try
        {
            File.AppendAllText(
                @"C:\temp\index_startup_error.log",
                $"[{DateTime.Now:O}] {error}{Environment.NewLine}");
        }
        catch
        {
        }
    }

}
