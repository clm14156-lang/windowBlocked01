using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Agent.Services;

namespace FocusApp.Agent;

internal sealed class TrayApplication : IDisposable
{
    private readonly TrayStateClient _stateClient;
    private readonly TrayIconHost _iconHost;
    private readonly Dispatcher _dispatcher;
    private readonly System.Windows.Application _application;
    private readonly DesktopProcessLauncher _desktopLauncher;
    private TrayPopupWindow? _popup;
    private bool _popupOpening;
    private bool _disposed;

    public TrayApplication(DesktopProcessLauncher desktopLauncher, Dispatcher dispatcher)
    {
        _desktopLauncher = desktopLauncher;
        _dispatcher = dispatcher;
        _application = System.Windows.Application.Current;
        _stateClient = new TrayStateClient();
        _stateClient.StateChanged += (_, _) => _dispatcher.BeginInvoke(UpdatePopup);
        _stateClient.RuntimeChanged += (_, _) => _dispatcher.BeginInvoke(UpdatePopup);
        _iconHost = new TrayIconHost(OpenDesktop, ShowPopup, () => _stateClient.IsForcedActive, ExitApplication);
        _ = _stateClient.StartAsync();
    }

    private async void ShowPopup()
    {
        if (_disposed || _popupOpening) return;
        _popupOpening = true;
        var anchor = _iconHost.GetIconRect() ?? GetCursorAnchor();
        try
        {
            await _stateClient.RefreshAsync();
            if (_popup is null)
            {
                _popup = new TrayPopupWindow(_stateClient, OpenDesktop, ExitApplication);
                _popup.Closed += (_, _) => _popup = null;
            }

            UpdatePopup();
            _popup.Show();
            PositionPopup(_popup, anchor);
            _popup.Activate();
        }
        finally
        {
            _popupOpening = false;
        }
    }

    private static TrayScreenRect GetCursorAnchor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        return new TrayScreenRect(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1);
    }

    private static void PositionPopup(TrayPopupWindow popup, TrayScreenRect anchor)
    {
        var anchorRect = new NativeMethods.Rect(anchor.Left, anchor.Top, anchor.Right, anchor.Bottom);
        var monitor = NativeMethods.MonitorFromRect(ref anchorRect, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var handle = new WindowInteropHelper(popup).EnsureHandle();
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);

        var dpi = NativeMethods.GetDpiForWindow(handle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        NativeMethods.GetWindowRect(handle, out var windowRect);
        var width = Math.Max(1, windowRect.Right - windowRect.Left);
        var height = Math.Max(1, windowRect.Bottom - windowRect.Top);
        var gap = Math.Max(2, (int)Math.Round(4 * scale));
        // The tray icon rect is the sole anchor: align the popup's lower-left
        // corner with the icon's upper-left corner, then clamp only when the
        // popup would leave the monitor bounds. Deliberately do not use the
        // work area here: the tray window is allowed to overlap the taskbar.
        var left = anchor.Left;
        var top = anchor.Top - height - gap;

        var minimumLeft = monitorInfo.Monitor.Left + gap;
        var maximumLeft = Math.Max(minimumLeft, monitorInfo.Monitor.Right - width - gap);
        var minimumTop = monitorInfo.Monitor.Top + gap;
        var maximumTop = Math.Max(minimumTop, monitorInfo.Monitor.Bottom - height - gap);
        left = Math.Clamp(left, minimumLeft, maximumLeft);
        top = Math.Clamp(top, minimumTop, maximumTop);
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void UpdatePopup() => _popup?.ApplyState();

    private void OpenDesktop()
    {
        using var process = _desktopLauncher.FindRunningDesktop();
        if (process is null)
        {
            try { _desktopLauncher.StartDesktop(); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            { Trace.TraceWarning(error.Message); }
        }
        else
        {
            var window = FindDesktopWindow(process);
            if (window != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(window, NativeMethods.SwRestore);
                NativeMethods.SetForegroundWindow(window);
            }
        }
        _popup?.Close();
    }

    private static IntPtr FindDesktopWindow(Process process)
    {
        process.Refresh();
        if (process.MainWindowHandle != IntPtr.Zero)
        {
            return process.MainWindowHandle;
        }

        var result = IntPtr.Zero;
        NativeMethods.EnumWindows((window, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId != (uint)process.Id || NativeMethods.GetWindow(window, NativeMethods.GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private void ExitApplication()
    {
        if (_stateClient.IsForcedActive) return;
        using var process = _desktopLauncher.FindRunningDesktop();
        try { process?.CloseMainWindow(); }
        catch (InvalidOperationException) { }
        _popup?.Close();
        _application.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _popup?.Close();
        _iconHost.Dispose();
        _stateClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;
        public const uint GwOwner = 4;
        public const uint MonitorDefaultToNearest = 2;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpNoActivate = 0x0010;
        public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] public readonly record struct Rect(int Left, int Top, int Right, int Bottom);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }
    }
}

internal readonly record struct TrayScreenRect(int Left, int Top, int Right, int Bottom);

public sealed class TrayStateClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private readonly NamedPipeIpcClient _client = new(IpcClientRole.Agent);
    private readonly CancellationTokenSource _cts = new();
    private LocalDataSnapshotDto? _snapshot;
    private FocusRuntimeStatusDto? _runtime;
    private FocusFeatureAccess? _focusFeatureAccess;

    public TrayStateClient()
    {
        _client.EventReceived += Client_EventReceived;
        _client.Disconnected += (_, _) =>
        {
            Interlocked.Exchange(ref _snapshot, null);
            _focusFeatureAccess = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? StateChanged;
    public event EventHandler? RuntimeChanged;
    public LocalDataSnapshotDto? Snapshot => _snapshot;
    public bool CanUseFocusFeatures => _client.IsConnected && _focusFeatureAccess is { CanUse: true } access &&
        Snapshot?.AccountSession == access.AccountSession;
    public bool IsForcedActive => _runtime is { IsForcedMode: true, SessionId: not null, State: not FocusRuntimeState.Idle } ||
        _snapshot?.FocusSessions.Any(IsActiveForced) == true;

    public Task StartAsync() => RefreshAsync();

    public async Task RefreshAsync()
    {
        try
        {
            await _client.ConnectAsync(RequestTimeout, _cts.Token);
            PublishSnapshot(await _client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
                IpcOperations.GetState, new EmptyPayload(), RequestTimeout, _cts.Token));
            _runtime = await _client.SendAsync<EmptyPayload, FocusRuntimeStatusDto>(
                IpcOperations.GetFocusRuntimeStatus, new EmptyPayload(), RequestTimeout, _cts.Token);
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
            await RefreshFocusFeatureAccessAsync();
        }
        catch (Exception error) when (error is IpcConnectionException or IpcRemoteException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task RefreshFocusFeatureAccessAsync()
    {
        try
        {
            _focusFeatureAccess = await _client.SendAsync<EmptyPayload, FocusFeatureAccess>(
                IpcOperations.GetFocusFeatureAccess, new EmptyPayload(), RequestTimeout, _cts.Token);
        }
        catch (Exception error) when (error is IpcConnectionException or IpcRemoteException or OperationCanceledException or ObjectDisposedException)
        {
            _focusFeatureAccess = null;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal Task RefreshPopupAccessAsync() => _client.IsConnected ? RefreshFocusFeatureAccessAsync() : RefreshAsync();

    internal async Task SetFocusFeatureAsync(FocusFeature feature, bool enabled)
    {
        if (!CanUseFocusFeatures || _focusFeatureAccess is not { } access) return;
        var result = await _client.SendAsync<SetFocusFeatureCommand, MutationResult>(
            IpcOperations.SetFocusFeature, new SetFocusFeatureCommand(feature, enabled, access.AccountSession),
            RequestTimeout, _cts.Token);
        PublishSnapshot(result.State);
    }

    private void PublishSnapshot(LocalDataSnapshotDto snapshot)
    {
        LocalDataSnapshotDto? previous;
        do
        {
            previous = _snapshot;
            if (previous is not null && previous.Revision > snapshot.Revision) return;
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, snapshot, previous), previous));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Client_EventReceived(object? sender, IpcEnvelope envelope)
    {
        if (envelope.Operation == IpcOperations.StateChanged)
        {
            PublishSnapshot(envelope.ReadPayload<StateChangedEvent>().State);
        }
        else if (envelope.Operation == IpcOperations.FocusRuntimeStateChanged)
        {
            _runtime = envelope.ReadPayload<FocusRuntimeStateChangedEvent>().Status;
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _client.DisposeAsync();
        _cts.Dispose();
    }

    private static bool IsActiveForced(LocalFocusSessionDto session)
        => session.IsForcedMode && session.Status is LocalFocusSessionStatusDto.Preparing or LocalFocusSessionStatusDto.Focusing;
}

internal sealed class TrayIconHost : IDisposable
{
    private const int WmTray = 0x8001;
    private const int NIMAdd = 0x00000000;
    private const int NIMDelete = 0x00000002;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int LeftButtonUp = 0x0202;
    private const int RightButtonUp = 0x0205;
    private readonly HwndSource _source;
    private readonly Action _openDesktop;
    private readonly Action _showPopup;
    private readonly Func<bool> _isForced;
    private readonly Action _exit;
    private readonly IntPtr _icon;
    private bool _disposed;

    public TrayIconHost(Action openDesktop, Action showPopup, Func<bool> isForced, Action exit)
    {
        _openDesktop = openDesktop; _showPopup = showPopup; _isForced = isForced; _exit = exit;
        _source = new HwndSource(new HwndSourceParameters("FocusAppTray") { Width = 0, Height = 0, PositionX = -32000, PositionY = -32000 });
        _source.AddHook(WndProc);
        _icon = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)32512);
        var data = new NotifyIconData { Size = Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1, Flags = NifMessage | NifIcon | NifTip, Callback = WmTray, Icon = _icon, Tip = "专注时光" };
        ShellNotifyIcon(NIMAdd, ref data);
    }

    public TrayScreenRect? GetIconRect()
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = Marshal.SizeOf<NotifyIconIdentifier>(),
            Window = _source.Handle,
            Id = 1
        };
        return ShellNotifyIconGetRect(ref identifier, out var rect) == 0
            ? new TrayScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmTray)
        {
            var action = lParam.ToInt32();
            if (action == LeftButtonUp) _openDesktop();
            else if (action == RightButtonUp) _showPopup();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = new NotifyIconData { Size = Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1 };
        ShellNotifyIcon(NIMDelete, ref data);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect rect);
    private static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData
    {
        public int Size; public IntPtr Window; public uint Id; public int Flags; public int Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NotifyIconIdentifier
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public Guid GuidItem;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
