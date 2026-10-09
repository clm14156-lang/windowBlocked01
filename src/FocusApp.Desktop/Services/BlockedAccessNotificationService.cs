using System.Windows;
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

    public static void SetFocusSession(FocusSessionViewModel? focusSession)
    {
        _sessionScope?.Dispose();
        _sessionScope = focusSession is null ? null : new BlockedAccessNotificationSessionScope(focusSession);
    }

    public static void Show(BlockedAccessNotificationData data)
    {
        ArgumentNullException.ThrowIfNull(data);

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
