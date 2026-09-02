using Index.Capture;

namespace Index.Platform;

/// <summary>Captures only the active MTT virtual monitor through the existing WGC adapter.</summary>
public sealed class MttVirtualDisplayCaptureSource : IVirtualDisplayFrameSource
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(5);
    private readonly IWindowsDisplayCatalog _displayCatalog;
    private readonly IDisplaySurfaceCapture _displayCapture;
    private readonly IMttVirtualDisplayController _displayController;

    public MttVirtualDisplayCaptureSource(
        IWindowsDisplayCatalog displayCatalog,
        IDisplaySurfaceCapture displayCapture,
        IMttVirtualDisplayController displayController)
    {
        _displayCatalog = displayCatalog
            ?? throw new ArgumentNullException(nameof(displayCatalog));
        _displayCapture = displayCapture
            ?? throw new ArgumentNullException(nameof(displayCapture));
        _displayController = displayController
            ?? throw new ArgumentNullException(nameof(displayController));
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

    public async Task<DisplaySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _displayController.AcquireAsync(
            VirtualDisplayMode.UltraHd60,
            cancellationToken).ConfigureAwait(false);
        var display = lease.Display;

        var png = await _displayCapture.TryCaptureMonitorPngAsync(
            display.MonitorHandle,
            CaptureTimeout,
            cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            throw new InvalidOperationException(
                $"Windows Graphics Capture could not capture '{display.DeviceName}'.");
        }

        // Re-enumerate after WGC returns. A mode or topology change can invalidate both the
        // HMONITOR and its coordinate mapping while a frame is in flight.
        var current = _displayCatalog.GetMttVirtualDisplayState().ActiveDisplay;
        if (current is null
            || !string.Equals(
                current.DisplayId,
                display.DisplayId,
                StringComparison.OrdinalIgnoreCase)
            || current.Bounds != display.Bounds)
        {
            throw new InvalidOperationException(
                "The virtual display topology changed while the frame was being captured.");
        }

        return new DisplaySnapshot
        {
            DisplayId = display.DisplayId,
            DeviceName = display.DeviceName,
            DisplayIndex = 0,
            IsPrimary = display.IsPrimary,
            Width = display.Bounds.Width,
            Height = display.Bounds.Height,
            DpiScale = display.DpiScale,
            Left = display.Bounds.Left,
            Top = display.Bounds.Top,
            PngData = png
        };
    }
}
