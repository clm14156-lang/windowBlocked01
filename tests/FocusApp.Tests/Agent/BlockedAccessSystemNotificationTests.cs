using Xunit;

namespace FocusApp.Tests.Agent;

public sealed class BlockedAccessSystemNotificationTests
{
    [Fact]
    public void Agent_DoesNotSendSystemNotificationsForBlockedAccess()
    {
        var agentWorkerSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Agent", "AgentWorker.cs"));
        var programSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Agent", "Program.cs"));

        Assert.DoesNotContain("IBlockedAccessNotifier", agentWorkerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_notifier.Show", agentWorkerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IBlockedAccessNotifier", programSource, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Agent", "BlockedAccessNotifier.cs")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
