using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusApp.Contracts;

namespace FocusApp.Desktop.Services;

public enum DesktopServiceConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    ProtocolMismatch,
    Unavailable
}

public sealed class DesktopServiceConnection : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _initialRetryDelay;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task? _runTask;
    private NamedPipeIpcClient? _client;
    private DesktopServiceConnectionStatus _status;
    private LocalDataSnapshotDto? _state;
    private IpcErrorResult? _lastError;
    private AccessControlStatusDto? _accessControlStatus;
    private FocusRuntimeStatusDto? _focusRuntimeStatus;
    private bool _disposed;

    public DesktopServiceConnection(
        string? pipeName = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? requestTimeout = null,
        TimeSpan? initialRetryDelay = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? IpcProtocol.DefaultPipeName : pipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
        _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(1);
        if (_connectTimeout <= TimeSpan.Zero || _requestTimeout <= TimeSpan.Zero || _initialRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }

        _synchronizationContext = SynchronizationContext.Current;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LocalDataSnapshotDto>? StateChanged;

    public event EventHandler<AccessControlStatusDto>? AccessControlStateChanged;

    public event EventHandler<FocusRuntimeStatusDto>? FocusRuntimeStateChanged;

    public event EventHandler<AccessBlockedEvent>? AccessBlocked;

    public DesktopServiceConnectionStatus Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public LocalDataSnapshotDto? State
    {
        get => _state;
        private set => SetField(ref _state, value);
    }

    public IpcErrorResult? LastError
    {
        get => _lastError;
        private set => SetField(ref _lastError, value);
    }

    public bool IsConnected => Status == DesktopServiceConnectionStatus.Connected;

    public AccessControlStatusDto? AccessControlStatus
    {
        get => _accessControlStatus;
        private set => SetField(ref _accessControlStatus, value);
    }

    public FocusRuntimeStatusDto? FocusRuntimeStatus
    {
        get => _focusRuntimeStatus;
        private set => SetField(ref _focusRuntimeStatus, value);
    }

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runTask ??= RunAsync(_lifetimeCancellation.Token);
        return Task.CompletedTask;
    }

    public Task<MutationResult> SaveTargetAsync(
        SaveTargetCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.SaveTarget, command, cancellationToken, requestId);

    public async Task<MutationResult> SetLaunchAtStartupAsync(
        bool enabled,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
    {
        try
        {
            return await SendMutationAsync(
                IpcOperations.SetLaunchAtStartup,
                new SetLaunchAtStartupCommand(enabled),
                cancellationToken,
                requestId);
        }
        catch (IpcRemoteException exception)
        {
            SetOnContext(() => LastError = exception.Error);
            throw;
        }
    }

    public Task<MutationResult> SetWindowsNotificationsAsync(bool enabled, CancellationToken cancellationToken = default, Guid? requestId = null)
        => SendMutationAsync(IpcOperations.SetWindowsNotifications, new SetWindowsNotificationsCommand(enabled), cancellationToken, requestId);

    public Task<MutationResult> DeleteTargetAsync(
        DeleteTargetCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.DeleteTarget, command, cancellationToken, requestId);

    public Task<MutationResult> ReplaceWebsiteRulesAsync(
        ReplaceWebsiteRulesCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.ReplaceWebsiteRules, command, cancellationToken, requestId);

    public Task<MutationResult> ReplaceApplicationRulesAsync(
        ReplaceApplicationRulesCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.ReplaceApplicationRules, command, cancellationToken, requestId);

    public Task<MutationResult> ReplaceAutomaticRulesAsync(
        ReplaceAutomaticRulesCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.ReplaceAutomaticRules, command, cancellationToken, requestId);

    public Task<MutationResult> ReplaceDurationPresetsAsync(
        ReplaceDurationPresetsCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.ReplaceDurationPresets, command, cancellationToken, requestId);

    public Task<MutationResult> SaveSettingsAsync(
        SaveSettingsCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(IpcOperations.SaveSettings, command, cancellationToken, requestId);

    public async Task<LocalDataSnapshotDto> RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        var client = GetConnectedClient();
        var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
            IpcOperations.GetState,
            new EmptyPayload(),
            _requestTimeout,
            cancellationToken);
        PublishState(state);
        return state;
    }

    public async Task<AccessControlStatusDto> ActivateAccessControlAsync(
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
    {
        var status = await GetConnectedClient().SendAsync<ActivateAccessControlCommand, AccessControlStatusDto>(
            IpcOperations.ActivateAccessControl,
            new ActivateAccessControlCommand(expiresAtUtc),
            _requestTimeout,
            cancellationToken,
            requestId);
        PublishAccessControlStatus(status);
        return status;
    }

    public async Task<AccessControlStatusDto> DeactivateAccessControlAsync(
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
    {
        var status = await GetConnectedClient().SendAsync<DeactivateAccessControlCommand, AccessControlStatusDto>(
            IpcOperations.DeactivateAccessControl,
            new DeactivateAccessControlCommand(),
            _requestTimeout,
            cancellationToken,
            requestId);
        PublishAccessControlStatus(status);
        return status;
    }

    public async Task<AccessControlStatusDto> RefreshAccessControlStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await GetConnectedClient().SendAsync<EmptyPayload, AccessControlStatusDto>(
            IpcOperations.GetAccessControlStatus,
            new EmptyPayload(),
            _requestTimeout,
            cancellationToken);
        PublishAccessControlStatus(status);
        return status;
    }

    public async Task<FocusSessionMutationResult> StartForcedFocusAsync(
        StartForcedFocusCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
    {
        var result = await GetConnectedClient().SendAsync<StartForcedFocusCommand, FocusSessionMutationResult>(
            IpcOperations.StartForcedFocus,
            command,
            _requestTimeout,
            cancellationToken,
            requestId);
        SetOnContext(() => PublishFocusMutation(result));
        return result;
    }

    public Task<MutationResult> RecordCompletedFocusAsync(
        LocalFocusSessionDto session,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(
            IpcOperations.RecordCompletedFocus,
            new RecordCompletedFocusCommand(session),
            cancellationToken,
            requestId);

    public Task<MutationResult> StartNormalFocusAsync(
        LocalFocusSessionDto session,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(
            IpcOperations.StartNormalFocus,
            new StartNormalFocusCommand(session),
            cancellationToken,
            requestId);

    public Task<MutationResult> UpdateFocusTasksAsync(
        UpdateFocusTasksCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
        => SendMutationAsync(
            IpcOperations.UpdateFocusTasks,
            command,
            cancellationToken,
            requestId);

    public async Task<FocusSessionMutationResult> UpdateForcedFocusTasksAsync(
        UpdateForcedFocusTasksCommand command,
        CancellationToken cancellationToken = default,
        Guid? requestId = null)
    {
        var result = await GetConnectedClient().SendAsync<UpdateForcedFocusTasksCommand, FocusSessionMutationResult>(
            IpcOperations.UpdateForcedFocusTasks,
            command,
            _requestTimeout,
            cancellationToken,
            requestId);
        SetOnContext(() => PublishFocusMutation(result));
        return result;
    }

    public async Task<FocusRuntimeStatusDto> RefreshFocusRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await GetConnectedClient().SendAsync<EmptyPayload, FocusRuntimeStatusDto>(
            IpcOperations.GetFocusRuntimeStatus,
            new EmptyPayload(),
            _requestTimeout,
            cancellationToken);
        PublishFocusRuntimeStatus(status);
        return status;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetimeCancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var retryDelay = _initialRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            SetOnContext(() => Status = DesktopServiceConnectionStatus.Connecting);
            var client = new NamedPipeIpcClient(IpcClientRole.Desktop, _pipeName);
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Disconnected += (_, _) => disconnected.TrySetResult();
            client.EventReceived += Client_EventReceived;
            try
            {
                await client.ConnectAsync(_connectTimeout, cancellationToken);
                var ping = await client.SendAsync<EmptyPayload, PingResponse>(
                    IpcOperations.Ping,
                    new EmptyPayload(),
                    _requestTimeout,
                    cancellationToken);
                if (ping.ProtocolVersion != IpcProtocol.CurrentVersion)
                {
                    SetOnContext(() => Status = DesktopServiceConnectionStatus.ProtocolMismatch);
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    continue;
                }

                _client = client;
                var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
                    IpcOperations.GetState,
                    new EmptyPayload(),
                    _requestTimeout,
                    cancellationToken);
                var accessControlStatus = await client.SendAsync<EmptyPayload, AccessControlStatusDto>(
                    IpcOperations.GetAccessControlStatus,
                    new EmptyPayload(),
                    _requestTimeout,
                    cancellationToken);
                var focusRuntimeStatus = await client.SendAsync<EmptyPayload, FocusRuntimeStatusDto>(
                    IpcOperations.GetFocusRuntimeStatus,
                    new EmptyPayload(),
                    _requestTimeout,
                    cancellationToken);
                SetOnContext(() =>
                {
                    LastError = null;
                    Status = DesktopServiceConnectionStatus.Connected;
                    PublishState(state);
                    PublishAccessControlStatus(accessControlStatus);
                    PublishFocusRuntimeStatus(focusRuntimeStatus);
                });
                retryDelay = _initialRetryDelay;
                await disconnected.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IpcRemoteException exception) when (
                exception.Error.Code == IpcErrorCode.ProtocolVersionMismatch)
            {
                SetOnContext(() =>
                {
                    LastError = exception.Error;
                    Status = DesktopServiceConnectionStatus.ProtocolMismatch;
                });
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
            {
                var error = exception is IpcRemoteException remote
                    ? remote.Error
                    : new IpcErrorResult(IpcErrorCode.ServiceUnavailable, exception.Message, true);
                SetOnContext(() =>
                {
                    LastError = error;
                    Status = DesktopServiceConnectionStatus.Unavailable;
                });
            }
            finally
            {
                if (ReferenceEquals(_client, client))
                {
                    _client = null;
                }

                client.EventReceived -= Client_EventReceived;
                await client.DisposeAsync();
            }

            await Task.Delay(retryDelay, cancellationToken);
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 15));
        }

        SetOnContext(() => Status = DesktopServiceConnectionStatus.Disconnected);
    }

    private async Task<MutationResult> SendMutationAsync<TCommand>(
        string operation,
        TCommand command,
        CancellationToken cancellationToken,
        Guid? requestId)
    {
        var client = GetConnectedClient();
        var result = await client.SendAsync<TCommand, MutationResult>(
            operation,
            command,
            _requestTimeout,
            cancellationToken,
            requestId);
        PublishState(result.State);
        return result;
    }

    private NamedPipeIpcClient GetConnectedClient()
        => _client is { IsConnected: true } client
            ? client
            : throw new IpcConnectionException("FocusApp 后台服务当前不可用，操作未执行。");

    private void Client_EventReceived(object? sender, IpcEnvelope envelope)
    {
        try
        {
            switch (envelope.Operation)
            {
                case IpcOperations.StateChanged:
                    var stateChanged = envelope.ReadPayload<StateChangedEvent>();
                    SetOnContext(() => PublishState(stateChanged.State));
                    break;
                case IpcOperations.AccessControlStateChanged:
                    var accessControlChanged = envelope.ReadPayload<AccessControlStateChangedEvent>();
                    SetOnContext(() => PublishAccessControlStatus(accessControlChanged.Status));
                    break;
                case IpcOperations.FocusRuntimeStateChanged:
                    var focusChanged = envelope.ReadPayload<FocusRuntimeStateChangedEvent>();
                    SetOnContext(() => PublishFocusRuntimeStatus(focusChanged.Status));
                    break;
                case IpcOperations.AccessBlocked:
                    var blocked = envelope.ReadPayload<AccessBlockedEvent>();
                    SetOnContext(() => AccessBlocked?.Invoke(this, blocked));
                    break;
            }
        }
        catch (IpcProtocolException exception)
        {
            SetOnContext(() => LastError = new IpcErrorResult(
                IpcErrorCode.InvalidRequest,
                exception.Message));
        }
    }

    private void PublishState(LocalDataSnapshotDto state)
    {
        if (State is not null && state.Revision < State.Revision)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void PublishAccessControlStatus(AccessControlStatusDto status)
    {
        AccessControlStatus = status;
        AccessControlStateChanged?.Invoke(this, status);
    }

    private void PublishFocusMutation(FocusSessionMutationResult result)
    {
        PublishState(result.State);
        PublishFocusRuntimeStatus(result.FocusStatus);
        PublishAccessControlStatus(result.AccessControlStatus);
    }

    private void PublishFocusRuntimeStatus(FocusRuntimeStatusDto status)
    {
        FocusRuntimeStatus = status;
        FocusRuntimeStateChanged?.Invoke(this, status);
    }

    private void SetOnContext(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return;
        }

        _synchronizationContext.Post(_ => action(), null);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(Status))
        {
            OnPropertyChanged(nameof(IsConnected));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
