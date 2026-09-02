using Index.Annotation;
using Index.Platform;
using Index.Storage;

namespace Index.Capture;

public enum VirtualDisplayAvailability
{
    NotInstalled,
    InstalledInactive,
    Active
}

public sealed record VirtualDisplayCaptureStatus(
    VirtualDisplayAvailability Availability,
    string? DisplayName = null,
    int Width = 0,
    int Height = 0);

public interface IVirtualDisplayFrameSource
{
    VirtualDisplayCaptureStatus GetStatus();

    Task<DisplaySnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}

public enum VirtualDisplayCaptureOutcomeKind
{
    Success,
    Busy,
    Unavailable,
    Canceled,
    Failed
}

public sealed record VirtualDisplayCaptureOutcome(
    VirtualDisplayCaptureOutcomeKind Kind,
    string Message,
    StoredCapture? StoredCapture = null,
    int Width = 0,
    int Height = 0)
{
    public bool IsSuccess => Kind == VirtualDisplayCaptureOutcomeKind.Success;
}

/// <summary>
/// Saves one complete virtual-display frame without creating an overlay on the invisible display.
/// The source owns Windows capture details; this workflow owns concurrency, persistence and
/// structured failure states.
/// </summary>
public sealed class VirtualDisplayCaptureWorkflow
{
    private readonly IVirtualDisplayFrameSource _frameSource;
    private readonly ISourceApplicationResolver _sourceApplicationResolver;
    private readonly ICaptureImagePreparer _imagePreparer;
    private readonly ICapturePersistenceService _persistenceService;
    private int _captureInProgress;

    public VirtualDisplayCaptureWorkflow(
        IVirtualDisplayFrameSource frameSource,
        ISourceApplicationResolver sourceApplicationResolver,
        ICaptureImagePreparer imagePreparer,
        ICapturePersistenceService persistenceService)
    {
        _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
        _sourceApplicationResolver = sourceApplicationResolver
            ?? throw new ArgumentNullException(nameof(sourceApplicationResolver));
        _imagePreparer = imagePreparer
            ?? throw new ArgumentNullException(nameof(imagePreparer));
        _persistenceService = persistenceService
            ?? throw new ArgumentNullException(nameof(persistenceService));
    }

    public VirtualDisplayCaptureStatus GetStatus() => _frameSource.GetStatus();

    public async Task<VirtualDisplayCaptureOutcome> CaptureAndSaveAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
        {
            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Busy,
                "已有一项虚拟屏截图正在进行。");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _frameSource.GetStatus();
            if (status.Availability == VirtualDisplayAvailability.NotInstalled)
            {
                return new VirtualDisplayCaptureOutcome(
                    VirtualDisplayCaptureOutcomeKind.Unavailable,
                    "未检测到 MTT Virtual Display Driver。");
            }

            var sourceSnapshot = _sourceApplicationResolver.CaptureSnapshot();
            var display = await _frameSource
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var selection = new CaptureSelection
            {
                Display = new CaptureDisplayIdentity(
                    display.DisplayId,
                    display.Left,
                    display.Top,
                    display.Width,
                    display.Height,
                    display.DpiScale),
                X = 0,
                Y = 0,
                Width = display.Width,
                Height = display.Height,
                Layers = new Layers<ImageSpace>()
            };
            var prepared = _imagePreparer.TryPrepareDirect(selection, display.PngData)
                ?? _imagePreparer.PrepareFrozen(selection, display.PngData);
            var globalRegion = new SourceWindowBounds(
                display.Left,
                display.Top,
                checked(display.Left + display.Width),
                checked(display.Top + display.Height));
            var stored = await _persistenceService.SaveAsync(
                new CapturePersistenceRequest(
                    prepared,
                    selection,
                    display,
                    globalRegion,
                    sourceSnapshot,
                    DateTimeOffset.Now),
                cancellationToken).ConfigureAwait(false);

            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Success,
                $"已保存 {display.Width}×{display.Height} 虚拟屏截图。",
                stored,
                display.Width,
                display.Height);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Canceled,
                "虚拟屏截图已取消。");
        }
        catch (Exception error)
        {
            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Failed,
                $"虚拟屏截图失败：{error.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _captureInProgress, 0);
        }
    }
}
