using Microsoft.Extensions.Hosting;

namespace FocusApp.Agent;

internal sealed class AgentWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
