using System.Drawing;
using FocusApp.Contracts;

namespace FocusApp.Agent;

internal interface IBlockedAccessNotifier : IAsyncDisposable
{
    void Show(AccessBlockedEvent blocked);
}

internal sealed class WindowsTrayBlockedAccessNotifier : IBlockedAccessNotifier
{
    private readonly Thread _uiThread;
    private readonly TaskCompletionSource<TrayApplicationContext> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public WindowsTrayBlockedAccessNotifier()
    {
        _uiThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "FocusApp Agent Notifications"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
    }

    public void Show(AccessBlockedEvent blocked)
    {
        if (!_disposed)
        {
            _ = ShowAsync(blocked);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var context = await _ready.Task;
        context.Exit();
        await Task.Run(_uiThread.Join);
    }

    private async Task ShowAsync(AccessBlockedEvent blocked)
    {
        var context = await _ready.Task;
        context.Show(blocked);
    }

    private void RunMessageLoop()
    {
        try
        {
            var context = new TrayApplicationContext();
            _ready.TrySetResult(context);
            System.Windows.Forms.Application.Run(context);
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
        }
    }

    private sealed class TrayApplicationContext : System.Windows.Forms.ApplicationContext
    {
        private readonly System.Windows.Forms.Control _dispatcher = new();
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

        public TrayApplicationContext()
        {
            _dispatcher.CreateControl();
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "FocusApp",
                Visible = true
            };
        }

        public void Show(AccessBlockedEvent blocked)
        {
            if (_dispatcher.IsDisposed)
            {
                return;
            }

            _dispatcher.BeginInvoke(() =>
            {
                _notifyIcon.BalloonTipTitle = "FocusApp 已阻止访问";
                _notifyIcon.BalloonTipText = blocked.Kind == BlockedTargetKind.Website
                    ? $"已阻止网站 {blocked.Target}"
                    : $"已阻止应用 {Path.GetFileName(blocked.Target)}";
                _notifyIcon.ShowBalloonTip(5000);
            });
        }

        public void Exit()
        {
            if (!_dispatcher.IsDisposed)
            {
                _dispatcher.BeginInvoke(ExitThread);
            }
        }

        protected override void ExitThreadCore()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }
}
