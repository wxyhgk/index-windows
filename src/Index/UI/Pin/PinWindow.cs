using System.Runtime.InteropServices.WindowsRuntime;
using Index.Actions;
using Index.Ocr;
using Index.Pin;
using Index.Platform.Clipboard;
using Index.Platform.Diagnostics;
using Index.Platform.Windowing;
using Index.Toolbar;
using Index.UI.Editor;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace Index.UI.Pin;

/// <summary>Borderless, always-on-top image window with a fixed-size overlay toolbar.</summary>
internal sealed class PinWindow : Window, ICaptureActionHost
{
    private readonly PinWindowModel _model;
    private readonly IAppDiagnostics _diagnostics;
    private readonly PinActionSession _actionSession;
    private readonly ToolbarRegistry _toolbarRegistry = new();
    private readonly ToolbarContext _toolbarContext;
    private readonly Grid _root;
    private readonly Border _imageBorder;
    private readonly Image _image;
    private readonly OcrTextOverlayView _ocrOverlay;
    private readonly PinToolbarWindow _toolbarWindow;
    private readonly TextBlock _status;
    private readonly PinInteractionState _interaction;
    private readonly NativePinPresentation _nativePresentation;
    private readonly PinOcrInteractionController _ocrController;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _toolbarHideTimer;
    private PinFallbackCaptionController? _fallbackCaption;
    private PinRect? _pendingVisibleFrame;
    private int _presentationStage;
    private TaskCompletionSource? _presentationCompletion;
    private bool _pinHovered;
    private bool _toolbarHovered;
    private bool _closed;

    public PinWindow(
        PinWindowModel model,
        CaptureActionRegistry actions,
        IOcrTextRecognizer ocrTextRecognizer,
        IClipboardWriter clipboardWriter,
        IAppDiagnostics diagnostics)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _interaction = new PinInteractionState(model.Pixels.Width, model.Pixels.Height);
        _nativePresentation = new NativePinPresentation(model.Pixels);
        _nativePresentation.FrameChanged += OnNativeFrameChanged;
        _nativePresentation.WheelChanged += OnNativeWheelChanged;
        _nativePresentation.CloseRequested += OnNativeCloseRequested;
        _nativePresentation.HoverChanged += OnNativeHoverChanged;
        _actionSession = new PinActionSession(
            actions ?? throw new ArgumentNullException(nameof(actions)),
            model.Artifact,
            model.Region,
            model.SuggestedFileName,
            this);
        _actionSession.ExecutionStateChanged += OnActionExecutionChanged;

        AppWindow.Title = "Index · 钉图";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Index.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        BuiltinToolbarControls.RegisterPinnedDefaults(_toolbarRegistry);
        _toolbarContext = new ToolbarContext(
            new Annotation.AnnotationState(),
            ToolbarScope.Pinned,
            PerformCommand,
            isActionExecuting: IsToolbarCommandExecuting,
            isActionEnabled: IsToolbarCommandEnabled);

        _image = new Image
        {
            Source = CreateReadySource(model.RenderedPng),
            // The frame already preserves the source aspect ratio. Fill maps the bitmap to
            // the exact client rectangle without Uniform introducing one-pixel letterboxing.
            Stretch = Stretch.Fill,
            UseLayoutRounding = true,
            IsHitTestVisible = false
        };
        _imageBorder = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Child = _image
        };
        _ocrOverlay = new OcrTextOverlayView(hitTargetsOnly: true)
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _toolbarWindow = new PinToolbarWindow(_toolbarRegistry, _toolbarContext);

        _status = new TextBlock
        {
            Text = "100%",
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var statusPlate = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB8, 0x16, 0x19, 0x20)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.82,
            IsHitTestVisible = false,
            Child = _status
        };

        _root = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent),
            IsTabStop = true
        };
        _root.Children.Add(_imageBorder);
        _root.Children.Add(_ocrOverlay);
        _root.Children.Add(statusPlate);
        Content = _root;

        _toolbarHideTimer = _root.DispatcherQueue.CreateTimer();
        _toolbarHideTimer.Interval = TimeSpan.FromMilliseconds(280);
        _toolbarHideTimer.IsRepeating = false;
        _toolbarHideTimer.Tick += OnToolbarHideTimerTick;
        _toolbarWindow.HoverChanged += OnToolbarHoverChanged;

        _ocrController = new PinOcrInteractionController(
            model,
            ocrTextRecognizer,
            clipboardWriter,
            diagnostics,
            _nativePresentation,
            _ocrOverlay,
            _root.DispatcherQueue,
            () => (_root.ActualWidth, _root.ActualHeight),
            () => _fallbackCaption?.EnableClientInteraction(),
            UpdateSelectionStatus,
            UpdateStatus,
            RefreshToolbar,
            Dismiss);

        _root.PointerEntered += OnRootPointerEntered;
        _root.PointerExited += OnRootPointerExited;
        _root.PointerWheelChanged += OnPointerWheelChanged;
        _root.KeyDown += OnKeyDown;
        _root.SizeChanged += OnRootSizeChanged;
        Activated += (_, _) => _root.Focus(FocusState.Programmatic);
        Closed += OnClosed;
    }

    public void StartAutomaticOcr()
        => _ocrController.Start();

    public Task ShowAsync()
    {
        if (_presentationCompletion is not null)
            throw new InvalidOperationException("The pin window has already been shown.");

        _presentationCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var region = _model.Region ?? new Capture.CaptureRegion(
            200, 160, _model.Pixels.Width, _model.Pixels.Height);
        var naturalFrame = new PinRect(
            region.X,
            region.Y,
            _model.Pixels.Width,
            _model.Pixels.Height);
        var workArea = PinWindowHost.WorkAreaAt(naturalFrame.MidX, naturalFrame.MidY);
        var initial = PinWindowGeometry.InitialFrame(naturalFrame, workArea);
        _interaction.Initialize(initial);

        if (TryShowNative(initial))
        {
            UpdateStatus();
            _presentationCompletion.SetResult();
            return _presentationCompletion.Task;
        }

        // Compatibility fallback for systems where layered-window creation fails.
        PinWindowHost.Configure(this, alwaysOnTop: false);
        _pendingVisibleFrame = initial;
        PinWindowHost.MoveAndResize(this, PinWindowHost.OffscreenWarmupFrame(initial));
        _presentationStage = 1;
        CompositionTarget.Rendered += OnPresentationFrameRendered;
        // WinUI creates and fills its swap chain only for a visible window. Activate it outside
        // the virtual desktop, then reveal the already-rendered surface atomically.
        Activate();
        _fallbackCaption = new PinFallbackCaptionController(
            this,
            _root,
            _toolbarWindow.Hide,
            _toolbarWindow.MoveAdjacent,
            frame =>
            {
                _toolbarWindow.MoveAdjacent(frame);
                SetPinHovered(true);
            });
        _fallbackCaption.Start();
        UpdateStatus();
        return _presentationCompletion.Task;
    }

    private bool TryShowNative(PinRect initial)
    {
        try
        {
            _nativePresentation.Show(initial, _interaction.OpacityByte);
            _toolbarWindow.Initialize(
                _nativePresentation.Handle,
                _nativePresentation.CurrentFrame);
            return true;
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"Native pin surface unavailable: {error}");
            DisposeNativePresentation();
            return false;
        }
    }

    private void OnPresentationFrameRendered(object? sender, object args)
    {
        if (_presentationStage == 0 || _pendingVisibleFrame is not { } visibleFrame)
            return;

        if (_presentationStage == 1)
        {
            // The first off-screen frame has filled the swap chain. The frozen capture overlay
            // is still visible, so the ready image can replace it in one handoff.
            _presentationStage = 0;
            _pendingVisibleFrame = null;
            CompositionTarget.Rendered -= OnPresentationFrameRendered;
            PinWindowHost.MoveAndResize(this, visibleFrame);
            _fallbackCaption?.UpdateCaptionRegion();
            PinWindowHost.SetAlwaysOnTop(this, true);
            _toolbarWindow.Initialize(this, visibleFrame);
            _presentationCompletion?.TrySetResult();
        }
    }

    public void Dismiss()
    {
        if (!_closed)
        {
            // Close the WinUI-owned toolbar before destroying its native owner. Destroying the
            // owner first makes Windows tear down the toolbar HWND behind WinUI's back, and a
            // later Window.Close call can access the already-destroyed ABI object.
            _toolbarWindow.Dismiss();
            DisposeNativePresentation();
            Close();
        }
    }

    private static BitmapImage CreateReadySource(byte[] png)
    {
        if (png.Length == 0)
            throw new InvalidDataException("The pin PNG is empty.");

        // Decode the final PNG directly on the UI thread. The former Skia -> BGRA ->
        // WriteableBitmap path introduced another sampling surface on high-DPI displays.
        var source = new BitmapImage();
        using var memory = new MemoryStream(png, writable: false);
        using var stream = memory.AsRandomAccessStream();
        source.SetSource(stream);
        return source;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(_root).Properties.MouseWheelDelta;
        if (delta == 0) return;

        bool controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (_nativePresentation.IsActive)
        {
            HandleNativeWheel(delta, controlDown);
            e.Handled = true;
            return;
        }
        if (controlDown)
        {
            _interaction.StepOpacity(delta);
            _root.Opacity = _interaction.Opacity;
            _status.Text = $"透明度 {_interaction.Opacity * 100:0}%";
        }
        else
        {
            ApplyZoom(_interaction.SteppedZoom(delta));
        }
        e.Handled = true;
    }

    private void ApplyZoom(double zoom)
    {
        var current = CurrentImageFrame();
        var workArea = PinWindowHost.WorkAreaAt(current.MidX, current.MidY);
        var next = _interaction.ApplyZoom(current, zoom, workArea);
        PinRect displayFrame = next;
        if (_nativePresentation.IsActive)
        {
            _nativePresentation.Update(next, _interaction.OpacityByte);
            displayFrame = _nativePresentation.CurrentFrame;
        }
        else
        {
            PinWindowHost.MoveAndResize(this, next);
            _fallbackCaption?.UpdateCaptionRegion();
        }
        _toolbarWindow.MoveAdjacent(displayFrame);
        UpdateStatus();
    }

    private void HandleNativeWheel(int delta, bool controlDown)
    {
        if (!_nativePresentation.IsActive || delta == 0)
            return;
        if (!controlDown)
        {
            ApplyZoom(_interaction.SteppedZoom(delta));
            return;
        }

        _interaction.StepOpacity(delta);
        _nativePresentation.Update(
            PinHighlight.ImageFrame(_nativePresentation.CurrentFrame),
            _interaction.OpacityByte);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool controlDown = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (_ocrController.HandleKey(e.Key, controlDown))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Add:
                ApplyZoom(_interaction.Zoom * PinWindowGeometry.WheelStep);
                e.Handled = true;
                break;
            case VirtualKey.Subtract:
                ApplyZoom(_interaction.Zoom / PinWindowGeometry.WheelStep);
                e.Handled = true;
                break;
            case VirtualKey.Number0:
                ApplyZoom(1);
                e.Handled = true;
                break;
        }
    }

    private async void PerformCommand(string commandId)
    {
        try
        {
            if (commandId == ToolbarCommandIds.CopyText)
            {
                _ocrController.CopyAvailableText();
                return;
            }

            var result = await _actionSession.ExecuteAsync(commandId);
            if (result.Status == CaptureActionExecutionStatus.Failed)
                System.Diagnostics.Debug.WriteLine($"Pin action {commandId} failed: {result.Error}");
        }
        catch (Exception error)
        {
            _diagnostics.Write(
                AppDiagnosticLevel.Warning,
                "pin.toolbar",
                "command-failed",
                new Dictionary<string, string?> { ["commandId"] = commandId },
                error);
        }
    }

    private bool IsToolbarCommandExecuting(string commandId)
        => commandId == ToolbarCommandIds.CopyText
            ? false
            : _actionSession.IsExecuting(commandId);

    private bool IsToolbarCommandEnabled(string commandId)
        => commandId != ToolbarCommandIds.CopyText || _ocrController.CanCopyText;

    private void OnActionExecutionChanged(string id)
    {
        if (_root.DispatcherQueue.HasThreadAccess)
            RefreshToolbar();
        else
            _root.DispatcherQueue.TryEnqueue(RefreshToolbar);
    }

    private void RefreshToolbar()
    {
        TryUpdateToolbar(() => _toolbarWindow.Refresh(CurrentDisplayFrame()), "refresh-failed");
    }

    private PinRect CurrentDisplayFrame() =>
        _nativePresentation.IsActive
            ? _nativePresentation.CurrentFrame
            : PinWindowHost.CurrentFrame(this);

    private PinRect CurrentImageFrame() =>
        _nativePresentation.IsActive
            ? PinHighlight.ImageFrame(_nativePresentation.CurrentFrame)
            : PinWindowHost.CurrentFrame(this);

    private void OnNativeFrameChanged(PinRect frame) =>
        _toolbarWindow.MoveAdjacent(frame);

    private void OnNativeHoverChanged(bool hovering)
    {
        // NativePinSurface raises this from its WndProc. Even when that happens on the UI thread,
        // defer WinUI window operations until the native message has fully unwound.
        _root.DispatcherQueue.TryEnqueue(() => SetPinHovered(hovering));
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs args)
        => SetPinHovered(true);

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs args)
        => SetPinHovered(false);

    private void OnToolbarHoverChanged(bool hovering)
    {
        _toolbarHovered = hovering;
        UpdateToolbarVisibility();
    }

    private void SetPinHovered(bool hovering)
    {
        _pinHovered = hovering;
        UpdateToolbarVisibility();
    }

    private void UpdateToolbarVisibility()
    {
        if (_closed)
            return;

        if (_pinHovered || _toolbarHovered)
        {
            _toolbarHideTimer.Stop();
            TryUpdateToolbar(
                () => _toolbarWindow.ShowAdjacent(CurrentDisplayFrame()),
                "show-failed");
            return;
        }

        _toolbarHideTimer.Stop();
        _toolbarHideTimer.Start();
    }

    private void OnToolbarHideTimerTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        if (!_pinHovered && !_toolbarHovered)
            TryUpdateToolbar(_toolbarWindow.Hide, "hide-failed");
    }

    private void TryUpdateToolbar(Action action, string eventName)
    {
        if (_closed)
            return;

        try
        {
            action();
        }
        catch (Exception error)
        {
            _diagnostics.Write(
                AppDiagnosticLevel.Warning,
                "pin.toolbar",
                eventName,
                exception: error);
        }
    }

    private void OnNativeWheelChanged(int delta, bool controlDown) =>
        HandleNativeWheel(delta, controlDown);

    private void OnNativeCloseRequested() => Dismiss();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
        => _ocrController.ResizeFallback(args.NewSize.Width, args.NewSize.Height);

    private void UpdateSelectionStatus(int wordCount)
    {
        _status.Text = wordCount > 0
            ? $"已选 {wordCount} 项 · Ctrl+C 复制"
            : $"{_interaction.Zoom * 100:0}%";
    }

    private void DisposeNativePresentation()
    {
        _nativePresentation.FrameChanged -= OnNativeFrameChanged;
        _nativePresentation.WheelChanged -= OnNativeWheelChanged;
        _nativePresentation.CloseRequested -= OnNativeCloseRequested;
        _nativePresentation.HoverChanged -= OnNativeHoverChanged;
        _nativePresentation.Dispose();
    }

    private void UpdateStatus() => _status.Text = $"{_interaction.Zoom * 100:0}%";

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _toolbarHideTimer.Stop();
        _toolbarHideTimer.Tick -= OnToolbarHideTimerTick;
        _toolbarWindow.HoverChanged -= OnToolbarHoverChanged;
        _root.PointerEntered -= OnRootPointerEntered;
        _root.PointerExited -= OnRootPointerExited;
        _ocrController.Dispose();
        _root.SizeChanged -= OnRootSizeChanged;
        _toolbarWindow.Dismiss();
        DisposeNativePresentation();
        if (_presentationStage != 0)
        {
            _presentationStage = 0;
            _pendingVisibleFrame = null;
            CompositionTarget.Rendered -= OnPresentationFrameRendered;
            _presentationCompletion?.TrySetException(
                new InvalidOperationException("The pin window closed before presentation completed."));
        }
        _fallbackCaption?.Dispose();
        _fallbackCaption = null;
        _actionSession.ExecutionStateChanged -= OnActionExecutionChanged;
        Closed -= OnClosed;
    }
}
