using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FocusApp.Infrastructure.AccessControl;
using FocusApp.Agent.Services;
using System.Security.Principal;

namespace FocusApp.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var launchAfterSetup = args.Contains("--launch-after-setup", StringComparer.OrdinalIgnoreCase);
        var openDesktop = launchAfterSetup || args.Contains("--open", StringComparer.OrdinalIgnoreCase);
        if (launchAfterSetup)
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return;
        }

        using var instance = new Mutex(true, @"Local\FocusApp.Agent", out var acquired);
        if (!acquired)
        {
            if (openDesktop) new DesktopProcessLauncher().StartDesktop();
            return;
        }

        var hostArguments = args.Where(arg => !arg.Equals("--open", StringComparison.OrdinalIgnoreCase) &&
            !arg.Equals("--launch-after-setup", StringComparison.OrdinalIgnoreCase)).ToArray();
        var builder = Host.CreateApplicationBuilder(hostArguments);
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
        var desktopLauncher = host.Services.GetRequiredService<DesktopProcessLauncher>();
        using var tray = new TrayApplication(desktopLauncher, application.Dispatcher);
        application.Exit += (_, _) => host.StopAsync().GetAwaiter().GetResult();
        if (openDesktop) desktopLauncher.StartDesktop();
        application.Run();
        instance.ReleaseMutex();
    }
}
