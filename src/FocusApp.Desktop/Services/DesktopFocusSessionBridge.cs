using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Services;

public sealed class DesktopFocusSessionBridge : IDisposable
{
    private readonly HomePageViewModel _homePage;
    private readonly DesktopServiceConnection _connection;
    private readonly DesktopAccessControlBridge _accessControlBridge;
    private readonly SemaphoreSlim _taskUpdateGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _pendingStartCancellation;
    private Guid? _pendingStartingSessionId;
    private int _cancelRequested;
    private FocusTargetViewModel? _pendingStartTarget;
    private Task _pendingTaskUpdate = Task.CompletedTask;
    private bool _disposed;

    public DesktopFocusSessionBridge(
        HomePageViewModel homePage,
        DesktopServiceConnection connection,
        DesktopAccessControlBridge accessControlBridge)
    {
        _homePage = homePage ?? throw new ArgumentNullException(nameof(homePage));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _accessControlBridge = accessControlBridge ?? throw new ArgumentNullException(nameof(accessControlBridge));
        _homePage.SetForcedFocusStarter(StartForcedFocusAsync);
        _homePage.SetForcedFocusStartCanceller(CancelStartingAsync);
        _homePage.FocusSession.AuthoritativeTasksChanged += FocusSession_AuthoritativeTasksChanged;
        _connection.StateChanged += Connection_StateChanged;
    }

    public event EventHandler<string>? SynchronizationFailed;

#if DEBUG
    public Task<FocusSessionMutationResult> EndForcedFocusForDebugAsync(
        CancellationToken cancellationToken = default)
        => _connection.EndForcedFocusForDebugAsync(cancellationToken);
#endif

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _homePage.FocusSession.AuthoritativeTasksChanged -= FocusSession_AuthoritativeTasksChanged;
        _connection.StateChanged -= Connection_StateChanged;
        try
        {
            _pendingTaskUpdate.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
        {
            SynchronizationFailed?.Invoke(this, exception.Message);
        }
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private async Task<bool> StartForcedFocusAsync(
        int minutes,
        FocusTargetViewModel? target,
        Guid? automaticRuleId,
        DateTimeOffset? automaticOccurrenceStartedAtUtc)
    {
        if (!_connection.IsConnected)
        {
            throw new IpcConnectionException("后台服务不可用，强制专注未启动。");
        }

        using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _pendingStartCancellation = startCancellation;
        await _accessControlBridge.PersistRulesNowAsync(startCancellation.Token);
        _pendingStartTarget = target;
        try
        {
            var result = await _connection.StartForcedFocusAsync(
                new StartForcedFocusCommand(
                    checked(minutes * 60),
                    target?.TargetId,
                    target?.Name,
                    [],
                    automaticRuleId,
                    automaticOccurrenceStartedAtUtc),
                startCancellation.Token);
            _pendingStartingSessionId = result.State.FocusSessions
                .SingleOrDefault(session => session.IsForcedMode &&
                    session.Status is LocalFocusSessionStatusDto.Preparing or LocalFocusSessionStatusDto.Focusing)?.SessionId;
            return result.State.FocusSessions.Any(session =>
                session.IsForcedMode &&
                session.Status is LocalFocusSessionStatusDto.Preparing or LocalFocusSessionStatusDto.Focusing);
        }
        catch
        {
            _pendingStartTarget = null;
            throw;
        }
        finally
        {
            if (ReferenceEquals(_pendingStartCancellation, startCancellation))
            {
                _pendingStartCancellation = null;
            }
        }
    }

    private async Task CancelStartingAsync()
    {
        Interlocked.Exchange(ref _cancelRequested, 1);
        try
        {
            _pendingStartCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        var sessionId = _pendingStartingSessionId;
        Guid id;
        if (sessionId is { } known)
        {
            id = known;
        }
        else if (_connection.IsConnected)
        {
            try
            {
                var state = await _connection.RefreshStateAsync(_lifetimeCancellation.Token);
                id = state.FocusSessions.SingleOrDefault(session =>
                    session.IsForcedMode && session.Status == LocalFocusSessionStatusDto.Preparing)?.SessionId ?? Guid.Empty;
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or OperationCanceledException)
            {
                SynchronizationFailed?.Invoke(this, exception.Message);
                Interlocked.Exchange(ref _cancelRequested, 0);
                return;
            }
        }
        else
        {
            return;
        }

        if (id == Guid.Empty)
        {
            Interlocked.Exchange(ref _cancelRequested, 0);
            return;
        }

        _pendingStartingSessionId = null;
        try
        {
            await _connection.CancelForcedFocusStartingAsync(id, _lifetimeCancellation.Token);
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or OperationCanceledException)
        {
            SynchronizationFailed?.Invoke(this, exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _cancelRequested, 0);
        }
    }

    private void Connection_StateChanged(object? sender, LocalDataSnapshotDto state)
    {
        var active = state.FocusSessions.SingleOrDefault(session =>
            session.IsForcedMode &&
            session.Status is LocalFocusSessionStatusDto.Preparing or LocalFocusSessionStatusDto.Focusing);
        if (active is not null && Volatile.Read(ref _cancelRequested) == 0)
        {
            if (_homePage.IsStartingForcedFocus)
            {
                _pendingStartingSessionId = active.SessionId;
            }
            var target = _homePage.FocusSession.AuthoritativeSessionId == active.SessionId
                ? _homePage.FocusSession.ActiveTarget
                : _pendingStartTarget;
            _homePage.FocusSession.ApplyAuthoritativeSession(active, target);
            _pendingStartTarget = null;
            return;
        }

        if (_homePage.FocusSession.AuthoritativeSessionId is not { } sessionId)
        {
            return;
        }

        var completed = state.FocusSessions.SingleOrDefault(session =>
            session.SessionId == sessionId && session.Status == LocalFocusSessionStatusDto.Completed);
        if (completed is not null)
        {
            _homePage.FocusSession.ApplyAuthoritativeSession(
                completed,
                _homePage.FocusSession.ActiveTarget);
        }
    }

    private void FocusSession_AuthoritativeTasksChanged(object? sender, EventArgs e)
    {
        if (_homePage.FocusSession.AuthoritativeSessionId is not { } sessionId)
        {
            return;
        }

        var snapshots = _homePage.FocusSession.SessionCompletedTasks
            .Select((task, index) => new LocalFocusSessionTaskSnapshotDto(
                task.TaskId,
                task.Name,
                index))
            .ToArray();
        _pendingTaskUpdate = UpdateTasksSafeAsync(sessionId, snapshots);
    }

    private async Task UpdateTasksSafeAsync(
        Guid sessionId,
        IReadOnlyList<LocalFocusSessionTaskSnapshotDto> snapshots)
    {
        try
        {
            await _taskUpdateGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            try
            {
                if (!_connection.IsConnected)
                {
                    return;
                }

                await _connection.UpdateForcedFocusTasksAsync(
                    new UpdateForcedFocusTasksCommand(sessionId, snapshots),
                    _lifetimeCancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                _taskUpdateGate.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
        {
            SynchronizationFailed?.Invoke(this, exception.Message);
        }
    }
}
