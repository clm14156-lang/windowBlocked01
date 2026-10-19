using System.Windows;
using System.Windows.Threading;
using FocusApp.Desktop.Models;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;

namespace FocusApp.Desktop.Services;

/// <summary>
/// Presentation-only host for the blocked-access notification.
/// No blocking state or access-control behavior is performed here.
/// </summary>
public static class BlockedAccessNotificationService
{
    private static BlockedAccessNotificationWindow? _activeWindow;
    private static BlockedAccessNotificationSessionScope? _sessionScope;
    private static readonly WebsiteNotificationThrottle WebsiteThrottle = new();
    private static readonly object PendingSync = new();
    private static BlockedAccessNotificationData? _pendingNotification;
    private static bool _deliveryScheduled;
    private static bool _isShuttingDown;
    private static Dispatcher? _dispatcher;
    private static IFaviconService? _faviconService;
    private static IEnumerable<BlockingWebsiteItemViewModel> _websites = [];

    public static void ConfigureWebsiteIcons(
        IFaviconService faviconService,
        IEnumerable<BlockingWebsiteItemViewModel> websites)
    {
        _faviconService = faviconService;
        _websites = websites;
    }

    public static void SetFocusSession(FocusSessionViewModel? focusSession)
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _sessionScope?.Dispose();
        _sessionScope = focusSession is null ? null : new BlockedAccessNotificationSessionScope(focusSession);
    }

    public static void Show(BlockedAccessNotificationData data, bool notificationsEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        var eligible = !string.Equals(data.Type, "Website", StringComparison.OrdinalIgnoreCase) ||
            WebsiteThrottle.RecordRequest(data.Address);
        if (!notificationsEnabled || !eligible) return;

        var dispatcher = Application.Current?.Dispatcher ?? _dispatcher ?? Dispatcher.CurrentDispatcher;
        lock (PendingSync)
        {
            if (_isShuttingDown || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            // One replaceable pending item, rather than one Dispatcher callback per event.
            _pendingNotification = data;
            if (_deliveryScheduled) return;
            _deliveryScheduled = true;
        }
        if (dispatcher.CheckAccess()) DeliverPending();
        else dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(DeliverPending));
    }

    public static void Shutdown()
    {
        lock (PendingSync)
        {
            _isShuttingDown = true;
            _pendingNotification = null;
            _deliveryScheduled = false;
        }
        WebsiteThrottle.Dispose();
        _faviconService = null;
        _websites = [];
        SetFocusSession(null);
        _activeWindow?.Close();
    }

    private static void DeliverPending()
    {
        BlockedAccessNotificationData? data;
        lock (PendingSync)
        {
            data = _isShuttingDown ? null : _pendingNotification;
            _pendingNotification = null;
            _deliveryScheduled = false;
        }
        if (data is not null) ShowOnDispatcher(data);
    }

    private static void ShowOnDispatcher(BlockedAccessNotificationData data)
    {
        if (_sessionScope?.ShouldShow(data.Type) == false || _activeWindow?.IsActionsOpen == true)
            return;

        _activeWindow?.Close();

        var targetKindResourceKey = string.Equals(data.Type, "Website", StringComparison.OrdinalIgnoreCase)
            ? "BlockedAccessNotificationWebsiteKind"
            : "BlockedAccessNotificationApplicationKind";
        var targetKindDisplay = Application.Current?.TryFindResource(targetKindResourceKey) as string
            ?? data.Type;
        var scope = _sessionScope;
        var sessionToken = scope?.CurrentSessionToken;
        var viewModel = new BlockedAccessNotificationViewModel(
            data.Name,
            data.Address,
            data.Type,
            targetKindDisplay,
            sessionToken is null ? null : () => scope!.SuppressWebsiteNotifications(sessionToken));
        if (_faviconService is not null)
        {
            _ = viewModel.LoadFaviconAsync(_faviconService, _websites);
        }
        var window = new BlockedAccessNotificationWindow
        {
            DataContext = viewModel
        };

        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
            {
                _activeWindow = null;
            }
        };

        _activeWindow = window;
        window.Show();
    }
}
