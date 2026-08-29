using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FocusApp.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FocusApp.Agent;

internal sealed class TrayApplication : IDisposable
{
    private readonly TrayStateClient _stateClient;
    private readonly TrayIconHost _iconHost;
    private readonly Dispatcher _dispatcher;
    private readonly System.Windows.Application _application;
    private TrayPopupWindow? _popup;
    private bool _popupOpening;
    private bool _disposed;

    public TrayApplication(IServiceProvider services, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _application = System.Windows.Application.Current;
        _stateClient = new TrayStateClient();
        _stateClient.StateChanged += (_, _) => _dispatcher.BeginInvoke(UpdatePopup);
        _stateClient.RuntimeChanged += (_, _) => _dispatcher.BeginInvoke(UpdatePopup);
        _iconHost = new TrayIconHost(ShowPopup, () => _stateClient.IsForcedActive, ExitApplication);
        _ = _stateClient.StartAsync();
    }

    private async void ShowPopup()
    {
        if (_disposed || _popupOpening) return;
        _popupOpening = true;
        try
        {
            await _stateClient.RefreshAsync();
            if (_popup is null)
            {
                _popup = new TrayPopupWindow(_stateClient, OpenDesktop, ExitApplication);
                _popup.Closed += (_, _) => _popup = null;
            }

            UpdatePopup();
            NativeMethods.GetCursorPos(out var cursor);
            var workArea = SystemParameters.WorkArea;
            _popup.Left = Math.Max(workArea.Left, Math.Min(cursor.X - _popup.Width / 2, workArea.Right - _popup.Width));
            _popup.Top = Math.Max(workArea.Top, Math.Min(cursor.Y - _popup.Height - 8, workArea.Bottom - _popup.Height));
            _popup.Show();
            _popup.Activate();
        }
        finally
        {
            _popupOpening = false;
        }
    }

    private void UpdatePopup() => _popup?.ApplyState(_stateClient.Snapshot, _stateClient.RuntimeStatus);

    private void OpenDesktop()
    {
        var process = Process.GetProcessesByName("FocusApp.Desktop").FirstOrDefault();
        if (process is null)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "FocusApp.Desktop.exe");
            if (!File.Exists(path))
            {
                path = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "FocusApp.Desktop", "bin", "Release", "net8.0-windows", "FocusApp.Desktop.exe"));
            }
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(process.MainWindowHandle);
        }
        _popup?.Close();
    }

    private void ExitApplication()
    {
        if (_stateClient.IsForcedActive) return;
        foreach (var process in Process.GetProcessesByName("FocusApp.Desktop"))
        {
            try { process.CloseMainWindow(); } catch { }
        }
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
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; }
    }
}

public sealed class TrayStateClient : IAsyncDisposable
{
    private readonly NamedPipeIpcClient _client = new(IpcClientRole.Agent);
    private readonly CancellationTokenSource _cts = new();
    private LocalDataSnapshotDto? _snapshot;
    private FocusRuntimeStatusDto? _runtime;
    private bool _subscribed;
    public event EventHandler? StateChanged;
    public event EventHandler? RuntimeChanged;
    public LocalDataSnapshotDto? Snapshot => _snapshot;
    public FocusRuntimeStatusDto? RuntimeStatus => _runtime;
    public bool IsForcedActive => _snapshot?.FocusSessions.Any(IsActiveForced) == true;

    public async Task StartAsync()
    {
        try
        {
            await _client.ConnectAsync(TimeSpan.FromSeconds(3), _cts.Token);
            SubscribeEvents();
            await RefreshAsync();
        }
        catch { }
    }

    public async Task RefreshAsync()
    {
        if (!_client.IsConnected)
        {
            try
            {
                await _client.ConnectAsync(TimeSpan.FromSeconds(3), _cts.Token);
                SubscribeEvents();
            }
            catch { return; }
        }
        try
        {
            _snapshot = await _client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(IpcOperations.GetState, new EmptyPayload(), TimeSpan.FromSeconds(3), _cts.Token);
            _runtime = await _client.SendAsync<EmptyPayload, FocusRuntimeStatusDto>(IpcOperations.GetFocusRuntimeStatus, new EmptyPayload(), TimeSpan.FromSeconds(3), _cts.Token);
            StateChanged?.Invoke(this, EventArgs.Empty);
            RuntimeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    private void SubscribeEvents()
    {
        if (_subscribed) return;
        _client.EventReceived += Client_EventReceived;
        _subscribed = true;
    }

    private void Client_EventReceived(object? sender, IpcEnvelope envelope)
    {
        if (envelope.Operation == IpcOperations.StateChanged)
        {
            _snapshot = envelope.ReadPayload<StateChangedEvent>().State;
            StateChanged?.Invoke(this, EventArgs.Empty);
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
    private readonly Action _show;
    private readonly Func<bool> _isForced;
    private readonly Action _exit;
    private readonly IntPtr _icon;
    private bool _disposed;

    public TrayIconHost(Action show, Func<bool> isForced, Action exit)
    {
        _show = show; _isForced = isForced; _exit = exit;
        _source = new HwndSource(new HwndSourceParameters("FocusAppTray") { Width = 0, Height = 0, PositionX = -32000, PositionY = -32000 });
        _source.AddHook(WndProc);
        _icon = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)32512);
        var data = new NotifyIconData { Size = Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle, Id = 1, Flags = NifMessage | NifIcon | NifTip, Callback = WmTray, Icon = _icon, Tip = "专注时光" };
        ShellNotifyIcon(NIMAdd, ref data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmTray)
        {
            var action = lParam.ToInt32();
            if (action == RightButtonUp) _show();
            else if (action == LeftButtonUp) _show();
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
    private static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData
    {
        public int Size; public IntPtr Window; public uint Id; public int Flags; public int Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
    }
}
