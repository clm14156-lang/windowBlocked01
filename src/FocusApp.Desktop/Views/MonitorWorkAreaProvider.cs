using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FocusApp.Desktop.Views;

internal static class MonitorWorkAreaProvider
{
    private const uint MonitorDefaultToNearest = 2;

    public static FocusMonitorArea GetForWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return new FocusMonitorArea(SystemParameters.VirtualScreenWidth > 0
                ? new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
                : SystemParameters.WorkArea,
                SystemParameters.WorkArea);
        }

        var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var windowRect = new NativeRect();
        if (!GetWindowRect(handle, ref windowRect))
        {
            return new FocusMonitorArea(ToDipRect(info.Monitor, transform), ToDipRect(info.WorkArea, transform));
        }

        return new FocusMonitorArea(
            ToDipRect(info.Monitor, windowRect, window, transform),
            ToDipRect(info.WorkArea, windowRect, window, transform));
    }

    private static Rect ToDipRect(NativeRect value, Matrix transform)
    {
        var topLeft = transform.Transform(new Point(value.Left, value.Top));
        var bottomRight = transform.Transform(new Point(value.Right, value.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static Rect ToDipRect(
        NativeRect value,
        NativeRect windowRect,
        Window window,
        Matrix transform)
    {
        var topLeft = ToDipPoint(value.Left, value.Top, windowRect, window, transform);
        var bottomRight = ToDipPoint(value.Right, value.Bottom, windowRect, window, transform);
        return new Rect(topLeft, bottomRight);
    }

    private static Point ToDipPoint(
        int x,
        int y,
        NativeRect windowRect,
        Window window,
        Matrix transform)
    {
        var relativeDevicePoint = new Point(x - windowRect.Left, y - windowRect.Top);
        var relativeDipPoint = transform.Transform(relativeDevicePoint);
        return new Point(window.Left + relativeDipPoint.X, window.Top + relativeDipPoint.Y);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, ref NativeRect windowRect);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
