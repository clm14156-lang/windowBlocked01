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
                SetOnContext(() =>
                {
                    LastError = null;
                    Status = DesktopServiceConnectionStatus.Connected;
                    PublishState(state);
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
        if (envelope.Operation != IpcOperations.StateChanged)
        {
            return;
        }

        try
        {
            var stateChanged = envelope.ReadPayload<StateChangedEvent>();
            SetOnContext(() => PublishState(stateChanged.State));
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
