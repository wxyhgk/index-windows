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
using Index.Platform.Windowing;

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
    private static LocalRecognitionHost? _recognitionHost;
    private static SystemTrayIcon? _trayIcon;
    private static Task? _virtualWindowCaptureTask;

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
                var graphicsCapture = new WindowsGraphicsCaptureInterop();
                var displayCatalog = new WindowsDisplayCatalog();
                var screenFreezer = new ScreenFreezer(graphicsCapture, displayCatalog);
                var virtualDisplayController = new MttVirtualDisplayController(displayCatalog);
                var imagePreparer = new WindowsCaptureImagePreparer();
                var capturePersistence = new CapturePersistenceService(
                    _shotStore,
                    sourceResolver,
                    new WindowsBrowserSourceMetadataResolver());
                var virtualDisplayCapture = new VirtualDisplayCaptureWorkflow(
                    new MttVirtualDisplayCaptureSource(
                        displayCatalog,
                        graphicsCapture,
                        virtualDisplayController),
                    sourceResolver,
                    imagePreparer,
                    capturePersistence);
                var virtualWindowFrameSource = new MttVirtualWindowCaptureSource(
                    displayCatalog,
                    graphicsCapture,
                    virtualDisplayController,
                    new WindowsWindow4KStager());
                var virtualWindowCapture = new VirtualWindowCaptureWorkflow(
                    virtualWindowFrameSource,
                    sourceResolver,
                    imagePreparer,
                    capturePersistence);
                _coordinator = new CaptureCoordinator(
                    screenFreezer,
                    screenFreezer,
                    new CaptureActionExecutor(actionRegistry),
                    sourceResolver,
                    graphicsCapture,
                    virtualWindowFrameSource,
                    imagePreparer,
                    capturePersistence,
                    new CaptureActionContextFactory(),
                    _shortcutSettings);
                _coordinator.InitOnUiThread();
                var shotAssetReader = new WindowsShotAssetReader(_shotStore);
                _recognitionHost = new LocalRecognitionHost();
                _recognitionPlugins = new RecognitionPluginRegistry(
                    new IRecognitionPlugin[]
                    {
                        new MolGrapherClient(host: _recognitionHost)
                    });
                _mainWindow = new MainWindow(
                    _coordinator,
                    _shotStore,
                    _libraryOrganization,
                    _shortcutSettings,
                    virtualDisplayCapture,
                    _clipboardStore,
                    clipboardHistoryWriter,
                    shotAssetReader,
                    _recognitionPlugins);
                _mainWindow.Activate();
                InitializeTrayIcon(_mainWindow);
                _shortcutController = new GlobalShortcutController(
                    _shortcutSettings,
                    () =>
                    {
                        if (!_mainWindow.DispatcherQueue.TryEnqueue(
                                () => _ = _coordinator.BeginCaptureAsync("hotkey")))
                        {
                            LogStartupFailure(new InvalidOperationException(
                                "Could not dispatch the capture hotkey to the UI thread."));
                        }
                    },
                    () => StartVirtualWindowCapture(virtualWindowCapture),
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
                    _recognitionHost?.Dispose();
                    _trayIcon?.Dispose();
                    _trayIcon = null;
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

    private static void StartVirtualWindowCapture(VirtualWindowCaptureWorkflow workflow)
    {
        // CaptureForegroundAndSaveAsync freezes the foreground HWND synchronously before its first
        // await. Keep this callback on the hotkey thread so Index never becomes the capture target.
        var capture = workflow.CaptureForegroundAndSaveAsync();
        _virtualWindowCaptureTask = ObserveVirtualWindowCaptureAsync(capture);
    }

    private static async Task ObserveVirtualWindowCaptureAsync(
        Task<VirtualDisplayCaptureOutcome> capture)
    {
        try
        {
            var outcome = await capture.ConfigureAwait(false);
            Directory.CreateDirectory(@"C:\temp");
            await File.AppendAllTextAsync(
                @"C:\temp\index_capture.log",
                $"[{DateTime.Now:O}] 4K current window: {outcome.Kind}; " +
                $"{outcome.Width}x{outcome.Height}; {outcome.Message}{Environment.NewLine}")
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            LogStartupFailure(new InvalidOperationException(
                "The 4K current-window hotkey failed unexpectedly.",
                error));
        }
    }

    private static void InitializeTrayIcon(MainWindow mainWindow)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Index.ico");
            _trayIcon = new SystemTrayIcon(iconPath, "Index");
            _trayIcon.OpenRequested += (_, _) => mainWindow.ShowFromTray();
            _trayIcon.ExitRequested += (_, _) => mainWindow.ExitApplication();
            mainWindow.EnableCloseToTray();
        }
        catch (Exception error)
        {
            // If the shell icon cannot be created, closing the main window must still exit.
            LogStartupFailure(new InvalidOperationException("Could not initialize the Index tray icon.", error));
        }
    }

}
