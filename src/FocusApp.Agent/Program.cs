using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FocusApp.Infrastructure.AccessControl;

namespace FocusApp.Agent;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<IUserProxyManager, UserProxyManager>();
        builder.Services.AddHostedService<AgentWorker>();

        using var host = builder.Build();
        await host.RunAsync();
    }
}
