using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FocusApp.Infrastructure.Persistence;

namespace FocusApp.Service;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "FocusAppService";
        });
        builder.Services.AddSingleton<ILocalDataPathProvider, LocalDataPathProvider>();
        builder.Services.AddSingleton<IAccessControlExecutionHostFactory, AccessControlExecutionHostFactory>();
        builder.Services.AddSingleton<IUserStateCoordinatorProvider, UserStateCoordinatorProvider>();
        builder.Services.AddSingleton<IClientIdentityResolver, NamedPipeClientIdentityResolver>();
        builder.Services.AddHostedService<NamedPipeServiceWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
