using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using WinRT;

namespace Index.Platform;

public interface IWindowSurfaceCapture
{
    Task<byte[]?> TryCapturePngAsync(
        nint window,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface IDisplaySurfaceCapture
{
    Task<byte[]?> TryCaptureMonitorPngAsync(
        nint monitor,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Captures compositor-backed windows and displays as PNG frames through Windows Graphics Capture.
/// </summary>
public sealed class WindowsGraphicsCaptureInterop : IWindowSurfaceCapture, IDisplaySurfaceCapture
{
    private const string CaptureLogPath = @"C:\temp\index_capture.log";
    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public async Task<byte[]?> TryCapturePngAsync(
        nint window,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await TryCaptureTargetPngAsync(
            window,
            "hwnd",
            CreateItemForWindow,
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> TryCaptureMonitorPngAsync(
        nint monitor,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await TryCaptureTargetPngAsync(
            monitor,
            "monitor",
            CreateItemForMonitor,
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> TryCaptureTargetPngAsync(
        nint target,
        string targetKind,
        Func<nint, GraphicsCaptureItem?> createItem,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (target == 0 || !GraphicsCaptureSession.IsSupported())
            return null;

        try
        {
            var item = createItem(target);
            if (item is null || item.Size.Width <= 0 || item.Size.Height <= 0)
                return null;
            Log($"item created: {targetKind}=0x{target:X}, size={item.Size.Width}x{item.Size.Height}");

            using var canvasDevice = new CanvasDevice();
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                canvasDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            using var session = framePool.CreateCaptureSession(item);
            var frameReady = new TaskCompletionSource<Direct3D11CaptureFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
            {
                try
                {
                    var frame = sender.TryGetNextFrame();
                    if (frame is null)
                        return;
                    if (!frameReady.TrySetResult(frame))
                        frame.Dispose();
                }
                catch (Exception error)
                {
                    frameReady.TrySetException(error);
                }
            }

            framePool.FrameArrived += OnFrameArrived;
            session.StartCapture();
            Direct3D11CaptureFrame frame;
            try
            {
                frame = await frameReady.Task.WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                framePool.FrameArrived -= OnFrameArrived;
            }

            using (frame)
            using (var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(
                canvasDevice,
                frame.Surface))
            {
                using var stream = new InMemoryRandomAccessStream();
                await bitmap.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                stream.Seek(0);
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                Log($"frame encoded: {targetKind}=0x{target:X}, bytes={bytes.Length}");
                return bytes;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log($"capture canceled: {targetKind}=0x{target:X}");
            throw;
        }
        catch (Exception error)
        {
            Log($"capture failed: {targetKind}=0x{target:X}, " +
                $"{error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    private static GraphicsCaptureItem? CreateItemForWindow(nint window)
        => CreateItem(window, static (interop, target, iid) =>
            interop.CreateForWindow(target, iid));

    private static GraphicsCaptureItem? CreateItemForMonitor(nint monitor)
        => CreateItem(monitor, static (interop, target, iid) =>
            interop.CreateForMonitor(target, iid));

    private static GraphicsCaptureItem? CreateItem(
        nint target,
        Func<IGraphicsCaptureItemInterop, nint, Guid, nint> create)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        var itemPointer = create(interop, target, GraphicsCaptureItemGuid);
        if (itemPointer == 0)
            return null;
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                CaptureLogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] [wgc] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }
}
