using System.Runtime.InteropServices;

namespace Prowl.Launcher;

/// <summary>Keeps Windows dragging, resizing, and snapping while the launcher paints its title bar.</summary>
sealed class WindowFrame : IDisposable
{
    private readonly nint _handle;
    private readonly Func<float> _scale;
    private readonly Func<float> _controlsWidth;
    private readonly SubclassProcedure _procedure;
    private readonly Action<bool> _resizeChanged;

    public WindowFrame(nint handle, int width, int height, Func<float> scale, Func<float> controlsWidth, Action<bool> resizeChanged)
    {
        _handle = handle;
        _scale = scale;
        _controlsWidth = controlsWidth;
        _resizeChanged = resizeChanged;
        _procedure = HandleMessage;
        if (!SetWindowSubclass(handle, _procedure, Constants.WindowsMessages.SubclassId, 0))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        // Preserve the requested client size when removing the native frame.
        SetWindowPos(handle, 0, 0, 0, width, height, 0x0036);
    }

    private nint HandleMessage(nint handle, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message is Constants.WindowsMessages.EnterSizeMove or Constants.WindowsMessages.ExitSizeMove)
        {
            _resizeChanged(message == Constants.WindowsMessages.EnterSizeMove);
        }

        if (message == Constants.WindowsMessages.NcCalcSize)
        {
            // Maximized windows include an invisible resize border outside the work area.
            // Keep the client inside it so the custom title bar isn't clipped at the screen edge.
            MonitorInfo monitor = new()
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };
            if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
            {
                Rectangle client = Marshal.PtrToStructure<Rectangle>(lParam);
                bool coversWorkArea = client.Left <= monitor.Work.Left && client.Top <= monitor.Work.Top
                                                                       && client.Right >= monitor.Work.Right && client.Bottom >= monitor.Work.Bottom;
                if (IsZoomed(handle) || coversWorkArea)
                {
                    client.Left = Math.Max(client.Left, monitor.Work.Left);
                    client.Top = Math.Max(client.Top, monitor.Work.Top);
                    client.Right = Math.Min(client.Right, monitor.Work.Right);
                    client.Bottom = Math.Min(client.Bottom, monitor.Work.Bottom);
                    Marshal.StructureToPtr(client, lParam, false);
                }
            }
            return 0;
        }

        if (message == Constants.WindowsMessages.NcHitTest && GetWindowRect(handle, out Rectangle bounds))
        {
            int x = unchecked((short)(lParam.ToInt64() & 0xffff)) - bounds.Left;
            int y = unchecked((short)(lParam.ToInt64() >> 16 & 0xffff)) - bounds.Top;
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            float scale = _scale();
            int edge = Math.Max(4, (int)(5 * scale));
            if (!IsZoomed(handle))
            {
                bool left = x < edge, right = x >= width - edge;
                bool top = y < edge, bottom = y >= height - edge;
                if (top)
                {
                    return left ? 13 : right ? 14 : 12;
                }
                if (bottom)
                {
                    return left ? 16 : right ? 17 : 15;
                }
                if (left)
                {
                    return 10;
                }
                if (right)
                {
                    return 11;
                }
            }
            // Leave FPS, the language picker, and window controls in the client area.
            return y < Constants.Layout.TitleBarHeight * scale && x < width - _controlsWidth() * scale
                ? 2 // HTCAPTION: native move, double-click maximize, and snap.
                : 1; // HTCLIENT
        }

        if (message == Constants.WindowsMessages.GetMinMaxInfo)
        {
            MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            MonitorInfo monitor = new()
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };
            if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor))
            {
                info.MaxPosition = new Point(monitor.Work.Left - monitor.Bounds.Left, monitor.Work.Top - monitor.Bounds.Top);
                info.MaxSize = new Point(monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
            }
            info.MinTrackSize = new Point((int)(640 * _scale()), (int)(480 * _scale()));
            Marshal.StructureToPtr(info, lParam, false);
            return 0;
        }

        return DefSubclassProc(handle, message, wParam, lParam);
    }

    public void Dispose() => RemoveWindowSubclass(_handle, _procedure, Constants.WindowsMessages.SubclassId);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle Bounds, Work;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint handle, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint handle, SubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint handle, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint handle, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out Rectangle bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint handle);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
