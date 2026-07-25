using System.Runtime.InteropServices;

namespace AMK.DataVisualization.App.Services;

/// <summary>
/// Keeps a window from being shrunk below a share of the monitor it currently sits
/// on. Windows App SDK exposes no minimum-size property, so the window procedure is
/// subclassed and <c>WM_GETMINMAXINFO</c> is answered directly. The limit is
/// recomputed on every message, so moving the window to another monitor picks up
/// that monitor's resolution.
/// </summary>
internal sealed class MinimumWindowSize
{
    private const int WindowMessageGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SubclassId = 1;

    private readonly double widthFraction;
    private readonly double heightFraction;

    // The delegate is held in a field: the subclass keeps a native pointer to it,
    // so letting it be collected would tear down the window procedure.
    private readonly SubclassProc subclassProc;

    public MinimumWindowSize(double widthFraction, double heightFraction)
    {
        this.widthFraction = widthFraction;
        this.heightFraction = heightFraction;
        subclassProc = HandleMessage;
    }

    public void Attach(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            SetWindowSubclass(windowHandle, subclassProc, SubclassId, IntPtr.Zero);
        }
    }

    private IntPtr HandleMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint subclassId,
        IntPtr referenceData)
    {
        if (message == WindowMessageGetMinMaxInfo
            && lParam != IntPtr.Zero
            && TryResolveMinimumSize(windowHandle, out int minimumWidth, out int minimumHeight))
        {
            MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = Math.Max(info.MinTrackSize.X, minimumWidth);
            info.MinTrackSize.Y = Math.Max(info.MinTrackSize.Y, minimumHeight);
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    /// <summary>
    /// Minimum track size in physical pixels. <c>WM_GETMINMAXINFO</c> and the
    /// monitor rectangle use the same units, so no DPI conversion is involved.
    /// </summary>
    private bool TryResolveMinimumSize(IntPtr windowHandle, out int minimumWidth, out int minimumHeight)
    {
        minimumWidth = 0;
        minimumHeight = 0;

        IntPtr monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        MonitorInfo monitorInfo = new() { StructureSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        int monitorWidth = monitorInfo.MonitorRect.Right - monitorInfo.MonitorRect.Left;
        int monitorHeight = monitorInfo.MonitorRect.Bottom - monitorInfo.MonitorRect.Top;
        if (monitorWidth <= 0 || monitorHeight <= 0)
        {
            return false;
        }

        minimumWidth = (int)Math.Round(monitorWidth * widthFraction);
        minimumHeight = (int)Math.Round(monitorHeight * heightFraction);
        return true;
    }

    private delegate IntPtr SubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint subclassId,
        IntPtr referenceData);

    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProc callback,
        uint subclassId,
        IntPtr referenceData);

    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    private static extern IntPtr DefSubclassProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public WindowPoint Reserved;
        public WindowPoint MaxSize;
        public WindowPoint MaxPosition;
        public WindowPoint MinTrackSize;
        public WindowPoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int StructureSize;
        public WindowRect MonitorRect;
        public WindowRect WorkRect;
        public uint Flags;
    }
}
