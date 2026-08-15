using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FocusApp.Service;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "FocusApp Service";
        });
        builder.Services.AddHostedService<ServiceWorker>();

        using var host = builder.Build();
        await host.RunAsync();
    }
}
