using System.Runtime.InteropServices;
using Index.Pin;

namespace Index.Platform.Windowing;

/// <summary>
/// Win32 per-pixel-alpha surface for pinned screenshots. A layered popup remains invisible
/// until UpdateLayeredWindow has accepted its complete bitmap, avoiding the clear frame that
/// WinUI swap chains can expose while a new top-level window is first composed.
/// </summary>
internal sealed class NativePinSurface : IDisposable
{
    private const string WindowClassName = "Index.NativePinSurface";
    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const int SwShowNoActivate = 4;
    private const uint WmClose = 0x0010;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmWindowPosChanged = 0x0047;
    private const int HtCaption = 2;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcAlpha = 0x01;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    private static readonly object ClassGate = new();
    private static readonly Dictionary<nint, NativePinSurface> Surfaces = new();
    private static readonly WindowProcedure SharedWindowProcedure = WindowProc;
    private static ushort _windowClassAtom;

    private nint _hwnd;
    private bool _disposed;

    public NativePinSurface()
    {
        EnsureWindowClass();
        _hwnd = NativeMethods.CreateWindowEx(
            WsExLayered | WsExTopmost | WsExToolWindow,
            WindowClassName,
            "Index Pin",
            WsPopup,
            0,
            0,
            1,
            1,
            nint.Zero,
            nint.Zero,
            NativeMethods.GetModuleHandle(null),
            nint.Zero);
        if (_hwnd == nint.Zero)
            throw new InvalidOperationException(
                $"Unable to create native pin window: {Marshal.GetLastPInvokeError()}.");

        lock (Surfaces)
            Surfaces[_hwnd] = this;
    }

    public nint Handle => _hwnd;

    public event Action<PinRect>? FrameChanged;
    public event Action<int, bool>? WheelChanged;
    public event Action? CloseRequested;

    public PinRect CurrentFrame
    {
        get
        {
            ThrowIfDisposed();
            if (!NativeMethods.GetWindowRect(_hwnd, out var rect))
                throw new InvalidOperationException(
                    $"Unable to read native pin bounds: {Marshal.GetLastPInvokeError()}.");
            return new PinRect(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top);
        }
    }

    public void Show(
        PinRect frame,
        byte[] premultipliedBgra,
        int pixelWidth,
        int pixelHeight,
        byte opacity = byte.MaxValue)
    {
        ThrowIfDisposed();
        Present(frame, premultipliedBgra, pixelWidth, pixelHeight, opacity);
        if (!NativeMethods.ShowWindow(_hwnd, SwShowNoActivate))
        {
            // A zero result only means the window was previously hidden, which is expected on
            // the first show. UpdateLayeredWindow has already supplied the visible contents.
        }
    }

    public void Update(
        PinRect frame,
        byte[] premultipliedBgra,
        int pixelWidth,
        int pixelHeight,
        byte opacity = byte.MaxValue)
    {
        ThrowIfDisposed();
        Present(frame, premultipliedBgra, pixelWidth, pixelHeight, opacity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        nint hwnd = _hwnd;
        _hwnd = nint.Zero;
        if (hwnd != nint.Zero)
        {
            lock (Surfaces)
                Surfaces.Remove(hwnd);
            if (NativeMethods.IsWindow(hwnd))
                NativeMethods.DestroyWindow(hwnd);
        }
        GC.SuppressFinalize(this);
    }

    private void Present(
        PinRect frame,
        byte[] pixels,
        int pixelWidth,
        int pixelHeight,
        byte opacity)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        int expectedLength = checked(pixelWidth * pixelHeight * 4);
        if (pixels.Length != expectedLength)
            throw new ArgumentException(
                $"Expected {expectedLength} BGRA bytes, received {pixels.Length}.",
                nameof(pixels));

        int x = checked((int)Math.Round(frame.X));
        int y = checked((int)Math.Round(frame.Y));
        int width = Math.Max(1, checked((int)Math.Round(frame.Width)));
        int height = Math.Max(1, checked((int)Math.Round(frame.Height)));
        if (width != pixelWidth || height != pixelHeight)
            throw new ArgumentException("The layered bitmap must match the window frame size.");

        var bitmapInfo = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = pixelWidth,
                Height = -pixelHeight,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
                SizeImage = (uint)expectedLength
            }
        };

        nint memoryDc = NativeMethods.CreateCompatibleDC(nint.Zero);
        if (memoryDc == nint.Zero)
            throw new InvalidOperationException("Unable to create pin memory DC.");

        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;
        nint screenDc = nint.Zero;
        try
        {
            bitmap = NativeMethods.CreateDIBSection(
                memoryDc,
                ref bitmapInfo,
                DibRgbColors,
                out nint bits,
                nint.Zero,
                0);
            if (bitmap == nint.Zero || bits == nint.Zero)
                throw new InvalidOperationException(
                    $"Unable to allocate pin DIB: {Marshal.GetLastPInvokeError()}.");
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            screenDc = NativeMethods.GetDC(nint.Zero);
            var destination = new NativePoint(x, y);
            var size = new NativeSize(width, height);
            var source = new NativePoint(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = opacity,
                AlphaFormat = AcSrcAlpha
            };
            if (!NativeMethods.UpdateLayeredWindow(
                    _hwnd,
                    screenDc,
                    ref destination,
                    ref size,
                    memoryDc,
                    ref source,
                    0,
                    ref blend,
                    UlwAlpha))
            {
                throw new InvalidOperationException(
                    $"Unable to present native pin pixels: {Marshal.GetLastPInvokeError()}.");
            }
        }
        finally
        {
            if (screenDc != nint.Zero)
                NativeMethods.ReleaseDC(nint.Zero, screenDc);
            if (previousBitmap != nint.Zero)
                NativeMethods.SelectObject(memoryDc, previousBitmap);
            if (bitmap != nint.Zero)
                NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memoryDc);
        }
    }

    private static void EnsureWindowClass()
    {
        if (_windowClassAtom != 0) return;
        lock (ClassGate)
        {
            if (_windowClassAtom != 0) return;
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(SharedWindowProcedure),
                Instance = NativeMethods.GetModuleHandle(null),
                Cursor = NativeMethods.LoadCursor(nint.Zero, new nint(32512)),
                ClassName = WindowClassName
            };
            _windowClassAtom = NativeMethods.RegisterClassEx(ref windowClass);
            if (_windowClassAtom == 0)
                throw new InvalidOperationException(
                    $"Unable to register native pin class: {Marshal.GetLastPInvokeError()}.");
        }
    }

    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        NativePinSurface? surface;
        lock (Surfaces)
            Surfaces.TryGetValue(hwnd, out surface);

        if (surface is not null)
        {
            switch (message)
            {
                case WmNcHitTest:
                    return new nint(HtCaption);
                case WmMouseWheel:
                    int delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    bool control = (wParam.ToInt64() & 0x0008) != 0;
                    surface.WheelChanged?.Invoke(delta, control);
                    return nint.Zero;
                case WmWindowPosChanged:
                    try
                    {
                        surface.FrameChanged?.Invoke(surface.CurrentFrame);
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    break;
                case WmClose:
                    surface.CloseRequested?.Invoke();
                    return nint.Zero;
                case WmNcDestroy:
                    lock (Surfaces)
                        Surfaces.Remove(hwnd);
                    break;
            }
        }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || _hwnd == nint.Zero, this);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint parameter);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(nint hwnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        internal static extern nint LoadCursor(nint instance, nint cursorName);

        [DllImport("user32.dll")]
        internal static extern nint GetDC(nint hwnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(nint hwnd, nint dc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateLayeredWindow(
            nint hwnd,
            nint destinationDc,
            ref NativePoint destination,
            ref NativeSize size,
            nint sourceDc,
            ref NativePoint source,
            uint colorKey,
            ref BlendFunction blend,
            uint flags);

        [DllImport("gdi32.dll")]
        internal static extern nint CreateCompatibleDC(nint dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateDIBSection(
            nint dc,
            ref BitmapInfo bitmapInfo,
            uint usage,
            out nint bits,
            nint section,
            uint offset);

        [DllImport("gdi32.dll")]
        internal static extern nint SelectObject(nint dc, nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(nint dc);
    }
}
