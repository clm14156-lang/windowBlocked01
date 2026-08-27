using System.Collections.Concurrent;
using FocusApp.Infrastructure.Persistence;

namespace FocusApp.Service;

public interface IUserStateCoordinatorProvider
{
    ServiceStateCoordinator GetForIdentity(string windowsIdentity);
}

public sealed class UserStateCoordinatorProvider : IUserStateCoordinatorProvider
{
    private readonly ILocalDataPathProvider _pathProvider;
    private readonly ConcurrentDictionary<string, ServiceStateCoordinator> _coordinators =
        new(StringComparer.OrdinalIgnoreCase);

    public UserStateCoordinatorProvider(ILocalDataPathProvider pathProvider)
    {
        _pathProvider = pathProvider;
    }

    public ServiceStateCoordinator GetForIdentity(string windowsIdentity)
    {
        if (string.IsNullOrWhiteSpace(windowsIdentity))
        {
            throw new ArgumentException("Windows 客户端身份不能为空。", nameof(windowsIdentity));
        }

        return _coordinators.GetOrAdd(
            windowsIdentity,
            identity => new ServiceStateCoordinator(
                new SqliteLocalDataStore(_pathProvider.GetDatabasePath(identity))));
    }
}
