using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.Views;

internal static class MonitorWorkAreaProvider
{
    private const uint MonitorDefaultToNearest = 2;

    public static void RestorePosition(Window window, Window owner, FocusFloatingPosition? saved)
    {
        new WindowInteropHelper(window).EnsureHandle();
        var current = GetNativeMonitor(MonitorFromWindow(new WindowInteropHelper(owner).Handle, MonitorDefaultToNearest));
        var bounds = FocusFloatingPositionCalculator.Resolve(saved, GetNativeMonitors(), current, new Size(window.Width, window.Height));
        ApplyNativeBounds(window, bounds);
    }

    public static FocusFloatingPosition CapturePosition(Window window, Point? floatingTopLeft = null)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = GetNativeMonitor(MonitorFromWindow(handle, MonitorDefaultToNearest));
        var windowRect = new NativeRect();
        GetWindowRect(handle, ref windowRect);
        var dpi = GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96d : monitor.ScaleX;
        var left = windowRect.Left + ((floatingTopLeft?.X ?? window.Left) - window.Left) * scale;
        var top = windowRect.Top + ((floatingTopLeft?.Y ?? window.Top) - window.Top) * scale;
        return new FocusFloatingPosition(monitor.Name,
            (left - monitor.WorkAreaPixels.Left) / scale, (top - monitor.WorkAreaPixels.Top) / scale);
    }

    public static void EnsureVisible(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var current = GetNativeMonitor(MonitorFromWindow(handle, MonitorDefaultToNearest));
        var bounds = FocusFloatingPositionCalculator.Resolve(CapturePosition(window), [current], current, new Size(window.Width, window.Height));
        ApplyNativeBounds(window, bounds);
    }

    private static IReadOnlyList<FloatingMonitorWorkArea> GetNativeMonitors()
    {
        var monitors = new List<FloatingMonitorWorkArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr dc, ref NativeRect rectangle, IntPtr data) =>
        {
            monitors.Add(GetNativeMonitor(handle));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static FloatingMonitorWorkArea GetNativeMonitor(IntPtr handle)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
        if (handle == IntPtr.Zero || !GetMonitorInfoEx(handle, ref info))
            return new FloatingMonitorWorkArea("primary", SystemParameters.WorkArea, 1, 1);
        var scaleX = 1d;
        var scaleY = 1d;
        if (GetDpiForMonitor(handle, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
        {
            scaleX = dpiX / 96d;
            scaleY = dpiY / 96d;
        }
        return new FloatingMonitorWorkArea(info.DeviceName,
            new Rect(info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right - info.WorkArea.Left, info.WorkArea.Bottom - info.WorkArea.Top), scaleX, scaleY);
    }

    private static void ApplyNativeBounds(Window window, Rect bounds)
    {
        // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE: only move the window.
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
            (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top), 0, 0, 0x0015);
    }

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

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref NativeRect rectangle, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoEx(IntPtr monitorHandle, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
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
