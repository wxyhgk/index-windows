using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Index.Platform.Windowing;

/// <summary>
/// Owns a native Windows notification-area icon and its context menu.
/// The instance must be created and disposed on the application's UI thread.
/// </summary>
public sealed class SystemTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private const uint IconId = 1;
    private const uint OpenCommand = 1001;
    private const uint ExitCommand = 1002;

    private readonly string _windowClassName = $"Index.TrayIcon.{Guid.NewGuid():N}";
    private readonly WindowProcedure _windowProcedure;
    private readonly uint _taskbarCreatedMessage;
    private nint _windowHandle;
    private nint _iconHandle;
    private ushort _windowClassAtom;
    private bool _iconAdded;
    private bool _disposed;

    public SystemTrayIcon(string iconPath, string tooltip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);
        if (!File.Exists(iconPath))
            throw new FileNotFoundException("The notification-area icon was not found.", iconPath);

        _windowProcedure = OnWindowMessage;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        try
        {
            CreateMessageWindow();
            _iconHandle = NativeMethods.LoadImage(
                nint.Zero,
                iconPath,
                NativeMethods.ImageIcon,
                0,
                0,
                NativeMethods.LoadFromFile | NativeMethods.LoadDefaultSize);
            if (_iconHandle == nint.Zero)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not load the Index tray icon.");

            Tooltip = tooltip.Length <= 127 ? tooltip : tooltip[..127];
            AddIcon();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    private string Tooltip { get; set; } = "Index";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_iconAdded && _windowHandle != nint.Zero)
        {
            var data = CreateIconData();
            NativeMethods.ShellNotifyIcon(NativeMethods.NotifyDelete, ref data);
            _iconAdded = false;
        }

        if (_iconHandle != nint.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = nint.Zero;
        }

        if (_windowHandle != nint.Zero)
        {
            NativeMethods.DestroyWindow(_windowHandle);
            _windowHandle = nint.Zero;
        }

        if (_windowClassAtom != 0)
        {
            NativeMethods.UnregisterClass(_windowClassName, NativeMethods.GetModuleHandle(null));
            _windowClassAtom = 0;
        }
    }

    private void CreateMessageWindow()
    {
        var module = NativeMethods.GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = module,
            ClassName = _windowClassName
        };

        _windowClassAtom = NativeMethods.RegisterClassEx(ref windowClass);
        if (_windowClassAtom == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not register the Index tray window.");

        _windowHandle = NativeMethods.CreateWindowEx(
            0,
            _windowClassName,
            "Index notification area",
            0,
            0,
            0,
            0,
            0,
            NativeMethods.MessageOnlyWindow,
            nint.Zero,
            module,
            nint.Zero);
        if (_windowHandle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the Index tray window.");
    }

    private void AddIcon()
    {
        if (_disposed || _windowHandle == nint.Zero || _iconHandle == nint.Zero)
            return;

        var data = CreateIconData();
        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NotifyAdd, ref data))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not add Index to the notification area.");
        _iconAdded = true;
    }

    private NotifyIconData CreateIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        IconId = IconId,
        Flags = NativeMethods.NotifyMessage | NativeMethods.NotifyIcon | NativeMethods.NotifyTip,
        CallbackMessage = CallbackMessage,
        IconHandle = _iconHandle,
        Tip = Tooltip
    };

    private nint OnWindowMessage(nint window, uint message, nuint wordParameter, nint longParameter)
    {
        if (message == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            AddIcon();
            return nint.Zero;
        }

        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)longParameter.ToInt64());
            if (mouseMessage == NativeMethods.LeftButtonDoubleClick)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
                return nint.Zero;
            }

            if (mouseMessage == NativeMethods.RightButtonUp || mouseMessage == NativeMethods.ContextMenu)
            {
                ShowContextMenu();
                return nint.Zero;
            }
        }

        return NativeMethods.DefWindowProc(window, message, wordParameter, longParameter);
    }

    private void ShowContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
            return;

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MenuString, OpenCommand, "打开 Index");
            NativeMethods.SetMenuDefaultItem(menu, OpenCommand, false);
            NativeMethods.AppendMenu(menu, NativeMethods.MenuSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MenuString, ExitCommand, "退出");
            if (!NativeMethods.GetCursorPos(out var cursor))
                return;

            NativeMethods.SetForegroundWindow(_windowHandle);
            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TrackRightButton | NativeMethods.TrackReturnCommand,
                cursor.X,
                cursor.Y,
                _windowHandle,
                nint.Zero);
            NativeMethods.PostMessage(_windowHandle, 0, 0, nint.Zero);

            if (command == OpenCommand)
                OpenRequested?.Invoke(this, EventArgs.Empty);
            else if (command == ExitCommand)
                ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wordParameter, nint longParameter);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint IconId;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        internal const uint ImageIcon = 1;
        internal const uint LoadFromFile = 0x0010;
        internal const uint LoadDefaultSize = 0x0040;
        internal const uint NotifyAdd = 0;
        internal const uint NotifyDelete = 2;
        internal const uint NotifyMessage = 0x0001;
        internal const uint NotifyIcon = 0x0002;
        internal const uint NotifyTip = 0x0004;
        internal const uint LeftButtonDoubleClick = 0x0203;
        internal const uint RightButtonUp = 0x0205;
        internal const uint ContextMenu = 0x007B;
        internal const uint MenuString = 0;
        internal const uint MenuSeparator = 0x0800;
        internal const uint TrackRightButton = 0x0002;
        internal const uint TrackReturnCommand = 0x0100;
        internal static readonly nint MessageOnlyWindow = new(-3);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool UnregisterClass(string className, nint instance);

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
        internal static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProc(nint window, uint message, nuint wordParameter, nint longParameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint LoadImage(
            nint instance,
            string name,
            uint type,
            int desiredWidth,
            int desiredHeight,
            uint loadFlags);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(nint icon);

        [DllImport(
            "shell32.dll",
            EntryPoint = "Shell_NotifyIconW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        internal static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll")]
        internal static extern nint CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool AppendMenu(nint menu, uint flags, nuint itemId, string? itemText);

        [DllImport("user32.dll")]
        internal static extern bool SetMenuDefaultItem(nint menu, uint item, bool byPosition);

        [DllImport("user32.dll")]
        internal static extern bool DestroyMenu(nint menu);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern uint TrackPopupMenuEx(
            nint menu,
            uint flags,
            int x,
            int y,
            nint window,
            nint parameters);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(nint window, uint message, nuint wordParameter, nint longParameter);
    }
}
