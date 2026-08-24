using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Index.Platform.Windowing;

/// <summary>Keeps a secondary Index window above its owning main window without making it globally topmost.</summary>
internal static class OwnedWindowRelationship
{
    private const int GwlHwndParent = -8;

    public static bool Attach(Window owner, Window child)
    {
        nint ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        return Attach(ownerHwnd, child);
    }

    public static bool Attach(nint ownerHwnd, Window child)
    {
        nint childHwnd = WinRT.Interop.WindowNative.GetWindowHandle(child);
        if (ownerHwnd == nint.Zero || childHwnd == nint.Zero)
            return false;

        Marshal.SetLastPInvokeError(0);
        nint previousOwner = NativeMethods.SetWindowLongPtr(childHwnd, GwlHwndParent, ownerHwnd);
        return previousOwner != nint.Zero || Marshal.GetLastPInvokeError() == 0;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }
}
