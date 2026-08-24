using System.Runtime.InteropServices;

namespace Index.Platform.Windowing;

/// <summary>Physical-pixel bounds used at the Win32 window boundary.</summary>
public readonly record struct OverlayPhysicalBounds(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
}

/// <summary>
/// Immutable diagnostics for applying the native overlay-window policy.
/// Error codes are Win32 values and are zero when the corresponding call succeeded.
/// </summary>
public sealed record OverlayWindowHostResult
{
    public required OverlayPhysicalBounds RequestedBounds { get; init; }
    public OverlayPhysicalBounds? ActualBounds { get; init; }

    public nint OriginalStyle { get; init; }
    public nint AppliedStyle { get; init; }
    public nint OriginalExtendedStyle { get; init; }
    public nint AppliedExtendedStyle { get; init; }

    public bool StyleApplied { get; init; }
    public bool ExtendedStyleApplied { get; init; }
    public bool PositionApplied { get; init; }
    public bool ActualBoundsRead { get; init; }

    public int StyleErrorCode { get; init; }
    public int ExtendedStyleErrorCode { get; init; }
    public int PositionErrorCode { get; init; }
    public int BoundsErrorCode { get; init; }

    public bool Succeeded
        => StyleApplied && ExtendedStyleApplied && PositionApplied && ActualBoundsRead;
}

/// <summary>
/// Owns the Win32 policy for a capture overlay: borderless, absent from Alt-Tab,
/// topmost, positioned in physical pixels, and shown without stealing activation.
/// </summary>
public static class OverlayWindowHost
{
    private const int GwlStyle = -16;
    private const int GwlExtendedStyle = -20;

    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTopmost = 0x00000008L;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly nint HwndTopmost = new(-1);

    public static bool IsWindowUsable(nint hwnd) =>
        hwnd != nint.Zero && NativeMethods.IsWindow(hwnd);

    public static bool IsWindowShown(nint hwnd) =>
        hwnd != nint.Zero && NativeMethods.IsWindowVisible(hwnd);

    public static OverlayWindowHostResult Apply(nint hwnd, OverlayPhysicalBounds physicalBounds)
    {
        if (hwnd == nint.Zero)
            throw new ArgumentException("A valid native window handle is required.", nameof(hwnd));
        if (physicalBounds.Width <= 0 || physicalBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(physicalBounds),
                physicalBounds,
                "Overlay bounds must have a positive width and height.");

        bool styleRead = TryGetWindowLong(hwnd, GwlStyle, out nint originalStyle, out int styleReadError);
        nint borderlessStyle = styleRead
            ? FromStyleBits(ToStyleBits(originalStyle) & ~(WsCaption | WsThickFrame))
            : nint.Zero;
        int styleSetError = 0;
        bool styleApplied = styleRead
            && TrySetWindowLong(hwnd, GwlStyle, borderlessStyle, out styleSetError);
        int styleError = styleRead ? styleSetError : styleReadError;

        bool extendedStyleRead = TryGetWindowLong(
            hwnd,
            GwlExtendedStyle,
            out nint originalExtendedStyle,
            out int extendedStyleReadError);
        nint overlayExtendedStyle = extendedStyleRead
            ? FromStyleBits(ToStyleBits(originalExtendedStyle) | WsExToolWindow | WsExTopmost)
            : nint.Zero;
        int extendedStyleSetError = 0;
        bool extendedStyleApplied = extendedStyleRead
            && TrySetWindowLong(
                hwnd,
                GwlExtendedStyle,
                overlayExtendedStyle,
                out extendedStyleSetError);
        int extendedStyleError = extendedStyleRead
            ? extendedStyleSetError
            : extendedStyleReadError;

        Marshal.SetLastPInvokeError(0);
        bool positionApplied = NativeMethods.SetWindowPos(
            hwnd,
            HwndTopmost,
            physicalBounds.Left,
            physicalBounds.Top,
            physicalBounds.Width,
            physicalBounds.Height,
            SwpShowWindow | SwpNoActivate | SwpFrameChanged);
        int positionError = positionApplied ? 0 : Marshal.GetLastPInvokeError();

        Marshal.SetLastPInvokeError(0);
        bool actualBoundsRead = NativeMethods.GetWindowRect(hwnd, out NativeRect actualRect);
        int boundsError = actualBoundsRead ? 0 : Marshal.GetLastPInvokeError();
        OverlayPhysicalBounds? actualBounds = actualBoundsRead
            ? new OverlayPhysicalBounds(
                actualRect.Left,
                actualRect.Top,
                actualRect.Right - actualRect.Left,
                actualRect.Bottom - actualRect.Top)
            : null;

        return new OverlayWindowHostResult
        {
            RequestedBounds = physicalBounds,
            ActualBounds = actualBounds,
            OriginalStyle = originalStyle,
            AppliedStyle = borderlessStyle,
            OriginalExtendedStyle = originalExtendedStyle,
            AppliedExtendedStyle = overlayExtendedStyle,
            StyleApplied = styleApplied,
            ExtendedStyleApplied = extendedStyleApplied,
            PositionApplied = positionApplied,
            ActualBoundsRead = actualBoundsRead,
            StyleErrorCode = styleError,
            ExtendedStyleErrorCode = extendedStyleError,
            PositionErrorCode = positionError,
            BoundsErrorCode = boundsError
        };
    }

    private static bool TryGetWindowLong(nint hwnd, int index, out nint value, out int errorCode)
    {
        Marshal.SetLastPInvokeError(0);
        value = NativeMethods.GetWindowLongPtr(hwnd, index);
        errorCode = Marshal.GetLastPInvokeError();
        bool succeeded = value != nint.Zero || errorCode == 0;
        if (succeeded) errorCode = 0;
        return succeeded;
    }

    private static bool TrySetWindowLong(nint hwnd, int index, nint value, out int errorCode)
    {
        Marshal.SetLastPInvokeError(0);
        nint previousValue = NativeMethods.SetWindowLongPtr(hwnd, index, value);
        errorCode = Marshal.GetLastPInvokeError();
        bool succeeded = previousValue != nint.Zero || errorCode == 0;
        if (succeeded) errorCode = 0;
        return succeeded;
    }

    private static long ToStyleBits(nint value)
        => IntPtr.Size == sizeof(long) ? value.ToInt64() : value.ToInt32();

    private static nint FromStyleBits(long value)
        => IntPtr.Size == sizeof(long)
            ? new nint(value)
            : new nint(unchecked((int)value));

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
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern nint GetWindowLongPtr(nint hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            nint hwnd,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint hwnd);
    }
}
