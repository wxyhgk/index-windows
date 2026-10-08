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
using Index.UI.Gallery;
using Index.UI.Pin;
using Index.Storage;
using Index.Recognition;
using Index.Platform.Windowing;
using Index.Platform.Diagnostics;
using Index.Search;
using Index.Gallery;
using Index.Editor;
using System.Diagnostics;
using Index.Platform.Ocr;
using Index.Ocr;

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
    private static VirtualDisplayCaptureWorkflow? _virtualDisplayCapture;
    private static PaddleOcrTextRecognizer? _paddleOcr;
    private static Task? _ocrWarmupTask;
    private static Task? _virtualWindowCaptureTask;
    private static readonly object VirtualWindowCaptureGate = new();
    private static readonly CancellationTokenSource ApplicationLifetime = new();
    private static readonly IAppDiagnostics Diagnostics = new LocalAppDiagnostics();

    [STAThread]
    private static void Main(string[] args)
    {
        Application.Start((app) =>
        {
            if (Application.Current is { } currentApplication)
                currentApplication.UnhandledException += OnXamlUnhandledException;

            try
            {
                _shotStore = ShotStore.OpenDefaultAsync().GetAwaiter().GetResult();
                _clipboardStore = ClipboardHistoryStore.OpenDefaultAsync().GetAwaiter().GetResult();
                _libraryOrganization = LibraryOrganizationStore.OpenDefaultAsync().GetAwaiter().GetResult();
                _shortcutSettings = new ShortcutSettingsStore();
                var clipboardWriter = new WindowsClipboardWriter();
                _paddleOcr = new PaddleOcrTextRecognizer();
                var ocrTextRecognizer = new FallbackOcrTextRecognizer(
                    _paddleOcr,
                    new WindowsOcrTextRecognizer());
                var imageExporter = new WindowsImageExporter();
                var clipboardReplaySuppression = new ClipboardReplaySuppression();
                var clipboardHistoryWriter = new ClipboardHistoryReplayWriter(
                    clipboardWriter,
                    clipboardReplaySuppression);
                var sourceResolver = new WindowsSourceApplicationResolver();
                _clipboardPopup = new ClipboardPopupModule(
                    _clipboardStore,
                    clipboardHistoryWriter,
                    new WindowsClipboardPasteTarget(),
                    Diagnostics);
                _clipboardHistory = new ClipboardHistoryCoordinator(
                    new WindowsClipboardWatcher(),
                    new WindowsClipboardSnapshotReader(sourceResolver),
                    _clipboardStore,
                    clipboardReplaySuppression,
                    Diagnostics);
                var actionRegistry = new CaptureActionRegistry();
                _pinWindows = new PinWindowManager(
                    actionRegistry,
                    ocrTextRecognizer,
                    clipboardWriter,
                    Diagnostics);
                actionRegistry.Register(new CompleteCaptureAction());
                actionRegistry.Register(new CopyAction(clipboardWriter));
                actionRegistry.Register(new SaveAction(imageExporter));
                actionRegistry.Register(new CloseCaptureAction());
                actionRegistry.Register(new PinAction(_pinWindows));
                var actionExecutor = new CaptureActionExecutor(actionRegistry);
                var actionContextFactory = new CaptureActionContextFactory();
                var graphicsCapture = new WindowsGraphicsCaptureInterop(Diagnostics);
                var displayCatalog = new WindowsDisplayCatalog();
                var screenFreezer = new ScreenFreezer(graphicsCapture, displayCatalog);
                var virtualDisplayController = new MttVirtualDisplayController(displayCatalog);
                var imagePreparer = new WindowsCaptureImagePreparer();
                var capturePersistence = new CapturePersistenceService(
                    _shotStore,
                    sourceResolver,
                    new WindowsBrowserSourceMetadataResolver());
                _virtualDisplayCapture = new VirtualDisplayCaptureWorkflow(
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
                var highResolutionCapture = new HighResolutionCaptureOrchestrator(
                    virtualWindowFrameSource,
                    imagePreparer,
                    capturePersistence,
                    actionExecutor,
                    actionContextFactory);
                _coordinator = new CaptureCoordinator(
                    screenFreezer,
                    screenFreezer,
                    actionExecutor,
                    sourceResolver,
                    graphicsCapture,
                    highResolutionCapture,
                    imagePreparer,
                    capturePersistence,
                    actionContextFactory,
                    _shortcutSettings,
                    ocrTextRecognizer,
                    clipboardWriter,
                    Diagnostics);
                _coordinator.InitOnUiThread();
                var shotAssetReader = new WindowsShotAssetReader(_shotStore);
                var shotAssetOpener = new WindowsShotAssetOpener(_shotStore, shotAssetReader);
                var galleryCommandService = new GalleryShotCommandService(
                    _shotStore,
                    _libraryOrganization,
                    shotAssetReader,
                    shotAssetOpener,
                    shotAssetOpener,
                    new WindowsExternalUriOpener(),
                    clipboardHistoryWriter,
                    imageExporter);
                var editorSessions = new ShotEditorSessionFactory(
                    shotAssetReader,
                    _shotStore);
                var unifiedSearch = new UnifiedSearchService(
                    _shotStore,
                    _clipboardStore);
                var applicationsWorkspaceControllers =
                    new CapturedApplicationsWorkspaceControllerFactory(_shotStore);
                var libraryWorkspaceController = new LibraryWorkspaceController(
                    _shotStore,
                    _libraryOrganization);
                _recognitionHost = new LocalRecognitionHost();
                _recognitionPlugins = new RecognitionPluginRegistry(
                    new IRecognitionPlugin[]
                    {
                        new MolGrapherClient(host: _recognitionHost)
                    });
                _mainWindow = new MainWindow(
                    _coordinator,
                    _shotStore,
                    libraryWorkspaceController,
                    _shortcutSettings,
                    _virtualDisplayCapture,
                    _clipboardStore,
                    clipboardHistoryWriter,
                    unifiedSearch,
                    applicationsWorkspaceControllers,
                    shotAssetReader,
                    galleryCommandService,
                    editorSessions,
                    _recognitionPlugins);
                _mainWindow.Activate();
                StartOcrWarmup(_paddleOcr);
                InitializeTrayIcon(_mainWindow);
                _shortcutController = new GlobalShortcutController(
                    _shortcutSettings,
                    () =>
                    {
                        if (!_mainWindow.DispatcherQueue.TryEnqueue(
                                () => _ = _coordinator.BeginCaptureAsync("hotkey")))
                        {
                            LogRuntimeFailure(
                                "capture.hotkey",
                                "dispatch-failed",
                                new InvalidOperationException(
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
                    ShutdownCaptureWork();
                    _shortcutController?.Dispose();
                    _clipboardHistory?.Dispose();
                    _clipboardPopup?.Close();
                    _pinWindows?.Dispose();
                    _paddleOcr?.Dispose();
                    _paddleOcr = null;
                    _ocrWarmupTask = null;
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

    private static void OnXamlUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Diagnostics.Write(
            AppDiagnosticLevel.Error,
            "runtime.xaml",
            "unhandled-exception",
            exception: args.Exception);
    }

    private static void LogStartupFailure(Exception error) => Diagnostics.Write(
        AppDiagnosticLevel.Error,
        "startup",
        "startup-failed",
        exception: error);

    private static void StartOcrWarmup(PaddleOcrTextRecognizer recognizer)
    {
        _ocrWarmupTask = ObserveOcrWarmupAsync(recognizer, ApplicationLifetime.Token);
    }

    private static async Task ObserveOcrWarmupAsync(
        PaddleOcrTextRecognizer recognizer,
        CancellationToken cancellationToken)
    {
        try
        {
            // Let the main window paint and become interactive before loading the ONNX sessions.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            var started = Stopwatch.GetTimestamp();
            await recognizer.WarmUpAsync(cancellationToken).ConfigureAwait(false);
            Diagnostics.Write(
                AppDiagnosticLevel.Information,
                "ocr",
                "warmup-completed",
                new Dictionary<string, string?>
                {
                    ["elapsedMilliseconds"] = Stopwatch
                        .GetElapsedTime(started)
                        .TotalMilliseconds
                        .ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            // OCR remains usable through lazy retry and the Windows OCR fallback.
            Diagnostics.Write(
                AppDiagnosticLevel.Warning,
                "ocr",
                "warmup-failed",
                exception: error);
        }
    }

    private static void StartVirtualWindowCapture(VirtualWindowCaptureWorkflow workflow)
    {
        lock (VirtualWindowCaptureGate)
        {
            if (ApplicationLifetime.IsCancellationRequested
                || _virtualWindowCaptureTask is { IsCompleted: false })
            {
                return;
            }

            // CaptureForegroundAndSaveAsync freezes the foreground HWND synchronously before its
            // first await. Keep this callback on the hotkey thread so Index never becomes the
            // capture target.
            var capture = workflow.CaptureForegroundAndSaveAsync(ApplicationLifetime.Token);
            _virtualWindowCaptureTask = ObserveVirtualWindowCaptureAsync(capture);
        }
    }

    private static async Task ObserveVirtualWindowCaptureAsync(
        Task<VirtualDisplayCaptureOutcome> capture)
    {
        try
        {
            var outcome = await capture.ConfigureAwait(false);
            Diagnostics.Write(
                AppDiagnosticLevel.Information,
                "capture.4k-hotkey",
                "capture-completed",
                new Dictionary<string, string?>
                {
                    ["outcome"] = outcome.Kind.ToString(),
                    ["dimensions"] = $"{outcome.Width}x{outcome.Height}",
                });
        }
        catch (Exception error)
        {
            LogRuntimeFailure(
                "capture.4k-hotkey",
                "capture-failed",
                error);
        }
    }

    private static CaptureShutdownWork BeginVirtualWindowCaptureShutdown()
    {
        try
        {
            ApplicationLifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        Task? capture;
        lock (VirtualWindowCaptureGate)
            capture = _virtualWindowCaptureTask;

        return new CaptureShutdownWork(
            "current-window-hotkey",
            capture ?? Task.CompletedTask);
    }

    private static void ShutdownCaptureWork()
    {
        try
        {
            var shutdownStarted = Stopwatch.GetTimestamp();
            var totalBudget = TimeSpan.FromSeconds(30);
            var work = new[]
            {
                _coordinator?.BeginShutdown()
                    ?? new CaptureShutdownWork(
                        "overlay-high-resolution",
                        Task.CompletedTask),
                BeginVirtualWindowCaptureShutdown(),
                _virtualDisplayCapture?.BeginShutdown()
                    ?? new CaptureShutdownWork(
                        "virtual-display",
                        Task.CompletedTask)
            };
            var remainingBudget = totalBudget - Stopwatch.GetElapsedTime(shutdownStarted);
            if (remainingBudget < TimeSpan.Zero)
                remainingBudget = TimeSpan.Zero;
            var report = CaptureShutdownCoordinator
                .WaitAsync(work, remainingBudget)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            if (!report.CompletedWithinBudget)
            {
                Diagnostics.Write(
                    AppDiagnosticLevel.Warning,
                    "capture.shutdown",
                    "restoration-timeout",
                    new Dictionary<string, string?>
                    {
                        ["participants"] = string.Join(",", report.Pending)
                    });
            }

            foreach (var failure in report.Failures)
            {
                Diagnostics.Write(
                    AppDiagnosticLevel.Error,
                    "capture.shutdown",
                    "restoration-failed",
                    new Dictionary<string, string?>
                    {
                        ["participant"] = failure.Name
                    },
                    failure.Error);
            }
        }
        catch (Exception error)
        {
            LogRuntimeFailure(
                "capture.shutdown",
                "coordination-failed",
                error);
        }
    }

    private static void LogRuntimeFailure(
        string category,
        string eventName,
        Exception error) => Diagnostics.Write(
            AppDiagnosticLevel.Error,
            category,
            eventName,
            exception: error);

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
