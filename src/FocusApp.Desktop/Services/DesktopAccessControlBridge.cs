using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services;

public sealed class DesktopAccessControlBridge : IDisposable
{
    private readonly FocusSessionViewModel _focusSession;
    private readonly BlockingPageViewModel _blockingPage;
    private readonly DesktopServiceConnection _connection;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private readonly SemaphoreSlim _ruleSaveGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _disposed;
    private bool _applyingServiceState;
    private LocalDataSnapshotDto? _pendingServiceState;
    private bool _stateApplyScheduled;

    public DesktopAccessControlBridge(
        FocusSessionViewModel focusSession,
        BlockingPageViewModel blockingPage,
        DesktopServiceConnection connection)
    {
        _focusSession = focusSession;
        _blockingPage = blockingPage;
        _connection = connection;
        _focusSession.PropertyChanged += FocusSession_PropertyChanged;
        _blockingPage.BlockingChanged += BlockingPage_BlockingChanged;
        _connection.PropertyChanged += Connection_PropertyChanged;
        _connection.StateChanged += Connection_StateChanged;
    }

    public event EventHandler<string>? ExecutionFailed;

    public Task PersistRulesNowAsync(CancellationToken cancellationToken = default)
        => PersistRulesAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _focusSession.PropertyChanged -= FocusSession_PropertyChanged;
        _blockingPage.BlockingChanged -= BlockingPage_BlockingChanged;
        _connection.PropertyChanged -= Connection_PropertyChanged;
        _connection.StateChanged -= Connection_StateChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private void FocusSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusSessionViewModel.Stage))
        {
            _ = ReconcileSafeAsync();
        }
    }

    private void Connection_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopServiceConnection.IsConnected) && _connection.IsConnected)
        {
            _ = ReconcileSafeAsync();
        }
    }

    private void BlockingPage_BlockingChanged(object? sender, EventArgs e)
    {
        if (!_applyingServiceState)
        {
            _ = PersistRulesSafeAsync();
        }
    }

    private void Connection_StateChanged(object? sender, LocalDataSnapshotDto state)
    {
        _pendingServiceState = state;
        if (_stateApplyScheduled)
        {
            return;
        }

        _stateApplyScheduled = true;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ApplyPendingServiceState();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ApplyPendingServiceState));
    }

    private void ApplyPendingServiceState()
    {
        _stateApplyScheduled = false;
        var state = _pendingServiceState;
        _pendingServiceState = null;
        if (state is null || RulesEqual(state))
        {
            return;
        }

        _applyingServiceState = true;
        try
        {
            _blockingPage.ReplaceRules(
                state.WebsiteRules
                    .OrderBy(rule => rule.SortOrder)
                    .Select(rule => new BlockingWebsiteItemViewModel(
                        rule.Id,
                        rule.Name,
                        rule.Address,
                        rule.IsEnabled)),
                state.ApplicationRules
                    .OrderBy(rule => rule.SortOrder)
                    .Select(rule => new BlockingApplicationItemViewModel(
                        rule.Id,
                        rule.Name,
                        rule.Path,
                        rule.IsEnabled)));
        }
        finally
        {
            _applyingServiceState = false;
        }
    }

    private async Task ReconcileSafeAsync()
    {
        try
        {
            await ReconcileAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
        {
            ExecutionFailed?.Invoke(this, exception.Message);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!_connection.IsConnected)
        {
            return;
        }

        await _reconcileGate.WaitAsync(cancellationToken);
        try
        {
            if (_focusSession.IsServiceOwnedForcedSession)
            {
                return;
            }

            if (_focusSession.Stage == FocusFlowStage.Focusing)
            {
                await PersistRulesAsync(cancellationToken);
                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _focusSession.RemainingFocusSeconds));
                await _connection.ActivateAccessControlAsync(expiresAt, cancellationToken);
            }
            else if (_connection.AccessControlStatus?.State is
                     AccessControlRuntimeState.Active or
                     AccessControlRuntimeState.PartiallyActive or
                     AccessControlRuntimeState.ProxyConflict or
                     AccessControlRuntimeState.Activating or
                     AccessControlRuntimeState.Faulted)
            {
                await _connection.DeactivateAccessControlAsync(cancellationToken);
            }
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task PersistRulesSafeAsync()
    {
        try
        {
            await PersistRulesAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
        {
            ExecutionFailed?.Invoke(this, exception.Message);
        }
    }

    private async Task PersistRulesAsync(CancellationToken cancellationToken)
    {
        if (!_connection.IsConnected)
        {
            throw new IpcConnectionException("后台服务不可用，屏蔽规则未保存。");
        }

        await _ruleSaveGate.WaitAsync(cancellationToken);
        try
        {
            var persistedState = _connection.State;
            var websitesChanged = persistedState is null || !WebsiteRulesEqual(persistedState);
            var applicationsChanged = persistedState is null || !ApplicationRulesEqual(persistedState);
            if (!websitesChanged && !applicationsChanged)
            {
                return;
            }

            // Persist each rule kind independently. Sending an unchanged website
            // snapshot before an application-only change publishes an intermediate
            // service state with the application's old value, which recreates the
            // application rows and makes their toggles visibly bounce.
            if (websitesChanged)
            {
                var websites = _blockingPage.Websites.Select((item, index) =>
                    new LocalWebsiteRuleDto(item.Id, item.Name, item.Address, item.IsEnabled, index)).ToArray();
                await _connection.ReplaceWebsiteRulesAsync(
                    new ReplaceWebsiteRulesCommand(websites),
                    cancellationToken);
            }

            if (applicationsChanged)
            {
                var applications = _blockingPage.Applications.Select((item, index) =>
                    new LocalApplicationRuleDto(item.Id, item.Name, item.Path, item.IsEnabled, index)).ToArray();
                await _connection.ReplaceApplicationRulesAsync(
                    new ReplaceApplicationRulesCommand(applications),
                    cancellationToken);
            }
        }
        finally
        {
            _ruleSaveGate.Release();
        }
    }

    private bool RulesEqual(LocalDataSnapshotDto state)
        => WebsiteRulesEqual(state) && ApplicationRulesEqual(state);

    private bool WebsiteRulesEqual(LocalDataSnapshotDto state)
    {
        var websites = _blockingPage.Websites;
        return websites.Count == state.WebsiteRules.Count &&
               websites.Select((item, index) => (item.Id, item.Name, item.Address, item.IsEnabled, index))
                   .SequenceEqual(state.WebsiteRules.OrderBy(rule => rule.SortOrder)
                       .Select(rule => (rule.Id, rule.Name, rule.Address, rule.IsEnabled, rule.SortOrder)));
    }

    private bool ApplicationRulesEqual(LocalDataSnapshotDto state)
    {
        var applications = _blockingPage.Applications;
        return applications.Count == state.ApplicationRules.Count &&
               applications.Select((item, index) => (item.Id, item.Name, item.Path, item.IsEnabled, index))
                   .SequenceEqual(state.ApplicationRules.OrderBy(rule => rule.SortOrder)
                       .Select(rule => (rule.Id, rule.Name, rule.Path, rule.IsEnabled, rule.SortOrder)));
    }
}
