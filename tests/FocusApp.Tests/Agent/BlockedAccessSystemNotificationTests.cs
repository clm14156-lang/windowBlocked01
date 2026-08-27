using Xunit;

namespace FocusApp.Tests.Agent;

public sealed class BlockedAccessSystemNotificationTests
{
    [Fact]
    public void AgentWorker_SendsSystemNotificationOnlyForApplicationBlocks()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "FocusApp.Agent", "AgentWorker.cs"));

        Assert.Contains(
            "if (blocked.Kind == BlockedTargetKind.Application)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("_notifier.Show(blocked);", source, StringComparison.Ordinal);
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
