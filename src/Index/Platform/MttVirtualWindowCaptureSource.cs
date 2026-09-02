using Index.Capture;

namespace Index.Platform;

/// <summary>
/// Temporarily renders one top-level window on the MTT 4K display at the same logical
/// size but higher pixel density, captures that HWND through WGC, then restores the window before
/// restoring display topology.
/// </summary>
public sealed class MttVirtualWindowCaptureSource : IVirtualWindowFrameSource
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(8);
    private readonly IWindowsDisplayCatalog _displayCatalog;
    private readonly IWindowSurfaceCapture _windowCapture;
    private readonly IMttVirtualDisplayController _displayController;
    private readonly IWindow4KStager _windowStager;

    public MttVirtualWindowCaptureSource(
        IWindowsDisplayCatalog displayCatalog,
        IWindowSurfaceCapture windowCapture,
        IMttVirtualDisplayController displayController,
        IWindow4KStager windowStager)
    {
        _displayCatalog = displayCatalog
            ?? throw new ArgumentNullException(nameof(displayCatalog));
        _windowCapture = windowCapture
            ?? throw new ArgumentNullException(nameof(windowCapture));
        _displayController = displayController
            ?? throw new ArgumentNullException(nameof(displayController));
        _windowStager = windowStager
            ?? throw new ArgumentNullException(nameof(windowStager));
    }

    public VirtualDisplayCaptureStatus GetStatus()
    {
        var state = _displayCatalog.GetMttVirtualDisplayState();
        var display = state.ActiveDisplay;
        return new VirtualDisplayCaptureStatus(
            state.Availability switch
            {
                MttVirtualDisplayAvailability.Active => VirtualDisplayAvailability.Active,
                MttVirtualDisplayAvailability.InstalledInactive =>
                    VirtualDisplayAvailability.InstalledInactive,
                _ => VirtualDisplayAvailability.NotInstalled
            },
            display?.DeviceName,
            display?.Bounds.Width ?? 0,
            display?.Bounds.Height ?? 0);
    }

    public async Task<VirtualWindowCaptureFrame> CaptureAsync(
        VirtualWindowCaptureTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.WindowHandle == nint.Zero || target.ProcessId == 0)
            throw new ArgumentException("A frozen window identity is required.", nameof(target));

        // Snapshot HWND placement and source DPI before display mutation. Enabling a remembered
        // virtual monitor can itself relocate or rescale existing windows.
        var stagingRequest = _windowStager.Prepare(target.WindowHandle);
        if (!Window4KStagingPolicy.IsSameWindowProcess(
                target.ProcessId,
                stagingRequest.ProcessId))
        {
            throw new InvalidOperationException(
                "The selected HWND no longer belongs to the frozen process.");
        }
        if (target.OriginalBounds.Width > 0
            && target.OriginalBounds.Height > 0
            && target.OriginalBounds != stagingRequest.OriginalBounds)
        {
            throw new InvalidOperationException(
                "The selected window moved or resized after the screen was frozen.");
        }
        await using var displayLease = await _displayController.AcquireAsync(
            VirtualDisplayMode.UltraHd60,
            cancellationToken).ConfigureAwait(false);
        var display = displayLease.Display;

        // These declarations intentionally share this scope. C# disposes them in reverse order,
        // restoring the HWND while the MTT display still exists, then restoring display topology.
        await using var windowLease = await _windowStager.StageAsync(
            stagingRequest,
            display.Bounds,
            display.DpiScale,
            cancellationToken).ConfigureAwait(false);

        var png = await _windowCapture.TryCapturePngAsync(
            windowLease.WindowHandle,
            CaptureTimeout,
            cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            throw new InvalidOperationException(
                "Windows Graphics Capture could not capture the staged window.");
        }

        var dimensions = PngImageHeader.Read(png);
        const int frameTolerance = 16;
        if (Math.Abs(dimensions.Width - windowLease.StagedBounds.Width) > frameTolerance
            || Math.Abs(dimensions.Height - windowLease.StagedBounds.Height) > frameTolerance)
        {
            throw new InvalidOperationException(
                $"WGC returned {dimensions.Width}×{dimensions.Height}; the density-preserving " +
                $"window size is {windowLease.StagedBounds.Width}×{windowLease.StagedBounds.Height} " +
                $"with a permitted frame-border difference of {frameTolerance} pixels.");
        }

        return new VirtualWindowCaptureFrame(
            new DisplaySnapshot
            {
                DisplayId = display.DisplayId,
                DeviceName = $"{display.DeviceName} · 4K window",
                DisplayIndex = 0,
                IsPrimary = display.IsPrimary,
                Width = dimensions.Width,
                Height = dimensions.Height,
                DpiScale = display.DpiScale,
                Left = windowLease.StagedBounds.Left,
                Top = windowLease.StagedBounds.Top,
                PngData = png
            },
            windowLease.OriginalBounds);
    }
}
