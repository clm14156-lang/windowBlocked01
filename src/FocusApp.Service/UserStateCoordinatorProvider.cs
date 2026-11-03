using System.Collections.Concurrent;
using FocusApp.Infrastructure.Persistence;
using FocusApp.Core;

namespace FocusApp.Service;

public interface IUserStateCoordinatorProvider
{
    ServiceStateCoordinator GetForIdentity(string windowsIdentity);
}

public sealed class UserStateCoordinatorProvider : IUserStateCoordinatorProvider, IAsyncDisposable
{
    private readonly ILocalDataPathProvider _pathProvider;
    private readonly IAccessControlExecutionHostFactory _accessControlFactory;
    private readonly ConcurrentDictionary<string, ServiceStateCoordinator> _coordinators =
        new(StringComparer.OrdinalIgnoreCase);

    public UserStateCoordinatorProvider(
        ILocalDataPathProvider pathProvider,
        IAccessControlExecutionHostFactory accessControlFactory)
    {
        _pathProvider = pathProvider;
        _accessControlFactory = accessControlFactory;
    }

    public ServiceStateCoordinator GetForIdentity(string windowsIdentity)
    {
        if (string.IsNullOrWhiteSpace(windowsIdentity))
        {
            throw new ArgumentException("Windows 客户端身份不能为空。", nameof(windowsIdentity));
        }

        return _coordinators.GetOrAdd(
            windowsIdentity,
            identity =>
            {
                var store = new SqliteLocalDataStore(_pathProvider.GetDatabasePath(identity));
                return new ServiceStateCoordinator(store, _accessControlFactory.Create(identity),
                    membershipAuthority: new MembershipAuthority(store, MembershipTrust.Resolve, MembershipTrust.MachineBinding(identity)));
            });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var coordinator in _coordinators.Values)
        {
            await coordinator.DisposeAsync();
        }
    }
}
