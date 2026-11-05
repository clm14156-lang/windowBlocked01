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
                var legacyPath = _pathProvider.GetDatabasePath(identity);
                var accountRoot = Path.Combine(Path.GetDirectoryName(legacyPath)!, "accounts");
                var store = new SqliteLocalDataStore(Path.Combine(accountRoot, "guest.db"));
                return new ServiceStateCoordinator(store, _accessControlFactory.Create(identity),
                    membershipAuthority: new MembershipAuthority(store, MembershipTrust.Resolve, MembershipTrust.MachineBinding(identity)),
                    initializeDeviceSettings: token => store.CopyDeviceSettingsAsync(legacyPath, token),
                    openAccountStore: async (userId, apiScope, token) =>
                    {
                        var accountStore = new SqliteLocalDataStore(Path.Combine(accountRoot, apiScope, userId.ToString("N") + ".db"));
                        await accountStore.CopyOwnedHistoryAsync(legacyPath, userId, apiScope, token);
                        return accountStore;
                    });
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
