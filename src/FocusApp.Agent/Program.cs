using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FocusApp.Infrastructure.AccessControl;
using FocusApp.Agent.Services;

namespace FocusApp.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new Mutex(true, @"Local\FocusApp.Agent", out var acquired);
        if (!acquired) return;

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<IUserProxyManager, UserProxyManager>();
        builder.Services.AddSingleton<StartupRegistrationService>();
        builder.Services.AddSingleton<DesktopProcessLauncher>();
        builder.Services.AddSingleton<IDesktopProcessLauncher>(services => services.GetRequiredService<DesktopProcessLauncher>());
        builder.Services.AddHostedService<AgentWorker>();

        using var host = builder.Build();
        host.StartAsync().GetAwaiter().GetResult();
        var application = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
        };
        using var tray = new TrayApplication(host.Services.GetRequiredService<DesktopProcessLauncher>(), application.Dispatcher);
        application.Exit += (_, _) => host.StopAsync().GetAwaiter().GetResult();
        application.Run();
        instance.ReleaseMutex();
    }
}
