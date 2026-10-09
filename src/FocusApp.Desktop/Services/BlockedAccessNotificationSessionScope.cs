using System.ComponentModel;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services;

/// <summary>Ephemeral website Toast preference; never changes access-control rules.</summary>
public sealed class BlockedAccessNotificationSessionScope : IDisposable
{
    private readonly FocusSessionViewModel _focusSession;
    private Guid? _authoritativeSessionId;
    private bool _websiteNotificationsSuppressed;

    public BlockedAccessNotificationSessionScope(FocusSessionViewModel focusSession)
    {
        _focusSession = focusSession ?? throw new ArgumentNullException(nameof(focusSession));
        _focusSession.PropertyChanged += FocusSession_PropertyChanged;
        UpdateSession();
    }

    public object? CurrentSessionToken { get; private set; }

    public bool ShouldShow(string type) =>
        !string.Equals(type, "Website", StringComparison.OrdinalIgnoreCase) ||
        !_websiteNotificationsSuppressed;

    public void SuppressWebsiteNotifications(object? sessionToken)
    {
        if (sessionToken is not null && ReferenceEquals(sessionToken, CurrentSessionToken))
            _websiteNotificationsSuppressed = true;
    }

    public void Dispose()
    {
        _focusSession.PropertyChanged -= FocusSession_PropertyChanged;
        CurrentSessionToken = null;
        _websiteNotificationsSuppressed = false;
    }

    private void FocusSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionViewModel.Stage) or nameof(FocusSessionViewModel.AuthoritativeSessionId))
            UpdateSession();
    }

    private void UpdateSession()
    {
        if (_focusSession.Stage is not (FocusFlowStage.Preparing or FocusFlowStage.Focusing))
        {
            CurrentSessionToken = null;
            _websiteNotificationsSuppressed = false;
        }
        else if (CurrentSessionToken is null || _authoritativeSessionId != _focusSession.AuthoritativeSessionId)
        {
            CurrentSessionToken = new object();
            _websiteNotificationsSuppressed = false;
        }
        _authoritativeSessionId = _focusSession.AuthoritativeSessionId;
    }
}
