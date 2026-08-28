using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FocusApp.Infrastructure.AccessControl;
using FocusApp.Agent.Services;

namespace FocusApp.Agent;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<IUserProxyManager, UserProxyManager>();
        builder.Services.AddSingleton<StartupRegistrationService>();
        var isAutostart = args.Any(argument => string.Equals(argument, StartupRegistrationService.AutostartArgument, StringComparison.OrdinalIgnoreCase));
        builder.Services.AddSingleton<AgentWorker>(provider => new AgentWorker(
            provider.GetRequiredService<ILogger<AgentWorker>>(),
            provider.GetRequiredService<IUserProxyManager>(),
            provider.GetRequiredService<StartupRegistrationService>(),
            provider.GetRequiredService<IHostApplicationLifetime>(),
            isAutostart));
        builder.Services.AddHostedService(provider => provider.GetRequiredService<AgentWorker>());

        using var host = builder.Build();
        await host.RunAsync();
    }
}
