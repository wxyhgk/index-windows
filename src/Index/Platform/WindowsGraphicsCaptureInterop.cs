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

/// <summary>Captures one compositor-backed HWND as a PNG without routing through the desktop.</summary>
public sealed class WindowsGraphicsCaptureInterop : IWindowSurfaceCapture
{
    private const string CaptureLogPath = @"C:\temp\index_capture.log";
    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public async Task<byte[]?> TryCapturePngAsync(
        nint window,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (window == 0 || !GraphicsCaptureSession.IsSupported())
            return null;

        try
        {
            var item = CreateItemForWindow(window);
            if (item is null || item.Size.Width <= 0 || item.Size.Height <= 0)
                return null;
            Log($"item created: hwnd=0x{window:X}, size={item.Size.Width}x{item.Size.Height}");

            using var canvasDevice = new CanvasDevice();
            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                canvasDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            using var session = framePool.CreateCaptureSession(item);
            var completion = new TaskCompletionSource<byte[]?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
            {
                try
                {
                    using var frame = sender.TryGetNextFrame();
                    if (frame is null) return;
                    sender.FrameArrived -= OnFrameArrived;
                    _ = EncodeFrameAsync(canvasDevice, frame.Surface, completion);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }

            framePool.FrameArrived += OnFrameArrived;
            session.StartCapture();
            try
            {
                var png = await completion.Task.WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);
                Log($"frame encoded: hwnd=0x{window:X}, bytes={png?.Length ?? 0}");
                return png;
            }
            finally
            {
                framePool.FrameArrived -= OnFrameArrived;
            }
        }
        catch (Exception error)
        {
            Log($"capture failed: hwnd=0x{window:X}, {error.GetType().Name}: {error.Message}");
            return null;
        }
    }

    private static async Task EncodeFrameAsync(
        CanvasDevice canvasDevice,
        Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface,
        TaskCompletionSource<byte[]?> completion)
    {
        try
        {
            using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(canvasDevice, surface);
            using var stream = new InMemoryRandomAccessStream();
            await bitmap.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            stream.Seek(0);
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            completion.TrySetResult(bytes);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private static GraphicsCaptureItem? CreateItemForWindow(nint window)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        var itemPointer = interop.CreateForWindow(window, GraphicsCaptureItemGuid);
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
