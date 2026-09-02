using System.Buffers.Binary;
using Index.Annotation;
using Index.Platform;
using Index.Storage;

namespace Index.Capture;

public readonly record struct PngImageDimensions(int Width, int Height);

public static class PngImageHeader
{
    private static ReadOnlySpan<byte> Signature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PngImageDimensions Read(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png[..8].SequenceEqual(Signature))
            throw new InvalidDataException("Capture output is not a PNG image.");
        if (!png.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("PNG does not begin with an IHDR chunk.");

        int width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG dimensions are invalid.");
        return new PngImageDimensions(width, height);
    }
}

public sealed record VirtualWindowCaptureFrame(
    DisplaySnapshot Display,
    SourceWindowBounds OriginalWindowBounds);

public sealed record VirtualWindowCaptureTarget(
    nint WindowHandle,
    uint ProcessId,
    SourceWindowBounds OriginalBounds);

public interface IVirtualWindowFrameSource
{
    VirtualDisplayCaptureStatus GetStatus();

    Task<VirtualWindowCaptureFrame> CaptureAsync(
        VirtualWindowCaptureTarget target,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Locks the foreground HWND before any display mutation, delegates temporary 4K rendering to a
/// Windows adapter, then persists the exact WGC window frame against the window's original source
/// bounds.
/// </summary>
public sealed class VirtualWindowCaptureWorkflow
{
    private readonly IVirtualWindowFrameSource _frameSource;
    private readonly ISourceApplicationResolver _sourceApplicationResolver;
    private readonly ICaptureImagePreparer _imagePreparer;
    private readonly ICapturePersistenceService _persistenceService;
    private int _captureInProgress;

    public VirtualWindowCaptureWorkflow(
        IVirtualWindowFrameSource frameSource,
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

    public async Task<VirtualDisplayCaptureOutcome> CaptureForegroundAndSaveAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _captureInProgress, 1, 0) != 0)
        {
            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Busy,
                "已有一项 4K 当前窗口截图正在进行。");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // This runs synchronously before the first await, so a global-hotkey caller locks the
            // user's target before Index or the temporary monitor can take focus.
            var sourceSnapshot = _sourceApplicationResolver.CaptureSnapshot();
            if (sourceSnapshot.ForegroundWindowHandle == nint.Zero)
            {
                return new VirtualDisplayCaptureOutcome(
                    VirtualDisplayCaptureOutcomeKind.Unavailable,
                    "Windows 没有可截取的前台窗口。");
            }

            var sourceWindow = sourceSnapshot.Windows.FirstOrDefault(window =>
                window.IsTopLevel
                && window.Handle == sourceSnapshot.ForegroundWindowHandle);
            if (sourceWindow is null)
            {
                return new VirtualDisplayCaptureOutcome(
                    VirtualDisplayCaptureOutcomeKind.Unavailable,
                    "Windows could not bind the foreground HWND to its frozen process identity.");
            }

            // The remainder can enumerate/change display topology. Leave the low-level keyboard
            // hook immediately so Windows does not remove it for exceeding its callback timeout.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (_frameSource.GetStatus().Availability == VirtualDisplayAvailability.NotInstalled)
            {
                return new VirtualDisplayCaptureOutcome(
                    VirtualDisplayCaptureOutcomeKind.Unavailable,
                    "未检测到 MTT Virtual Display Driver。");
            }

            var frame = await _frameSource.CaptureAsync(
                new VirtualWindowCaptureTarget(
                    sourceWindow.Handle,
                    sourceWindow.ProcessId,
                    sourceWindow.Bounds),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var display = frame.Display;
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
            var stored = await _persistenceService.SaveAsync(
                new CapturePersistenceRequest(
                    prepared,
                    selection,
                    display,
                    frame.OriginalWindowBounds,
                    sourceSnapshot,
                    DateTimeOffset.Now),
                cancellationToken).ConfigureAwait(false);

            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Success,
                $"已保存 {display.Width}×{display.Height} 当前窗口截图。",
                stored,
                display.Width,
                display.Height);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Canceled,
                "4K 当前窗口截图已取消。");
        }
        catch (Exception error)
        {
            return new VirtualDisplayCaptureOutcome(
                VirtualDisplayCaptureOutcomeKind.Failed,
                $"4K 当前窗口截图失败：{error.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _captureInProgress, 0);
        }
    }
}
