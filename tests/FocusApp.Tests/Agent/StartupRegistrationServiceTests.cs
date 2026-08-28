using FocusApp.Agent.Services;
using Xunit;

namespace FocusApp.Tests.Agent;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void EnableStartup_WritesQuotedAgentCommandWithAutostartArgument()
    {
        var store = new FakeStartupEntryStore();
        var service = new StartupRegistrationService(
            store,
            () => GetExistingAgentPath());

        var result = service.EnableStartup();

        Assert.True(result.Succeeded);
        Assert.Equal(
            StartupRegistrationService.BuildCommand(GetExistingAgentPath()),
            store.GetValue(StartupRegistrationService.EntryName));
        Assert.True(service.IsRegistered());
    }

    [Fact]
    public void DisableStartup_RemovesExistingEntry()
    {
        var store = new FakeStartupEntryStore();
        store.SetValue(StartupRegistrationService.EntryName, "stale");
        var service = new StartupRegistrationService(
            store,
            () => GetExistingAgentPath());

        var result = service.DisableStartup();

        Assert.True(result.Succeeded);
        Assert.Null(store.GetValue(StartupRegistrationService.EntryName));
    }

    [Fact]
    public void RepairStartupEntry_ReplacesStalePath()
    {
        var store = new FakeStartupEntryStore();
        store.SetValue(StartupRegistrationService.EntryName, "\"C:\\Old\\FocusApp.Agent.exe\" --autostart");
        var service = new StartupRegistrationService(
            store,
            () => GetExistingAgentPath());

        var result = service.RepairStartupEntry();

        Assert.True(result.Succeeded);
        Assert.Equal(
            StartupRegistrationService.BuildCommand(GetExistingAgentPath()),
            store.GetValue(StartupRegistrationService.EntryName));
    }

    [Fact]
    public void EnableStartup_WhenExecutableIsMissing_DoesNotWriteEntry()
    {
        var store = new FakeStartupEntryStore();
        var service = new StartupRegistrationService(store, () => @"C:\missing\FocusApp.Agent.exe");

        var result = service.EnableStartup();

        Assert.False(result.Succeeded);
        Assert.Null(store.GetValue(StartupRegistrationService.EntryName));
    }

    private static string GetExistingAgentPath()
        => typeof(StartupRegistrationService).Assembly.Location;

    private sealed class FakeStartupEntryStore : IStartupEntryStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValue(string name)
            => _values.TryGetValue(name, out var value) ? value : null;

        public void SetValue(string name, string value)
            => _values[name] = value;

        public void DeleteValue(string name)
            => _values.Remove(name);
    }
}
