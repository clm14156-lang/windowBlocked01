using Microsoft.Extensions.Hosting;

namespace FocusApp.Service;

internal sealed class ServiceWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
