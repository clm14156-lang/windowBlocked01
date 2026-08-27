using FocusApp.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FocusApp.Agent;

internal sealed class AgentWorker : BackgroundService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(ILogger<AgentWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var client = new NamedPipeIpcClient(IpcClientRole.Agent);
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.Disconnected += (_, _) => disconnected.TrySetResult();
            try
            {
                await client.ConnectAsync(ConnectTimeout, stoppingToken);
                var ping = await client.SendAsync<EmptyPayload, PingResponse>(
                    IpcOperations.Ping,
                    new EmptyPayload(),
                    RequestTimeout,
                    stoppingToken);
                var state = await client.SendAsync<EmptyPayload, LocalDataSnapshotDto>(
                    IpcOperations.GetState,
                    new EmptyPayload(),
                    RequestTimeout,
                    stoppingToken);
                _logger.LogInformation(
                    "Agent 已连接 FocusApp Service {ServiceInstanceId}，协议版本 {ProtocolVersion}，状态版本 {Revision}。",
                    ping.ServiceInstanceId,
                    ping.ProtocolVersion,
                    state.Revision);
                retryDelay = TimeSpan.FromSeconds(1);
                await disconnected.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException)
            {
                _logger.LogWarning(exception, "Agent 暂时无法连接 FocusApp Service。");
            }

            await Task.Delay(retryDelay, stoppingToken);
            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 15));
        }
    }
}
