using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class RecentProgramRefreshTests
{
    [Fact]
    public async Task ReopeningReadsFreshResultsAndKeepsSearchSelectionAndSaveWorking()
    {
        var now = DateTime.Now;
        var source = new ControlledSource();
        var page = new BlockingPageViewModel([], [], "Websites {0}", "Applications {0}", recentProgramSource: source);
        var modal = page.ProgramModal;
        page.OpenProgramModalCommand.Execute(null);
        source.Pending[0].SetResult([new(@"C:\Apps\Editor.exe", "Editor.exe", "Editor", now.AddHours(-1))]);
        await modal.RefreshTask;
        Assert.Equal("Editor", Assert.Single(modal.RecentPrograms).DisplayName);
        modal.CloseCommand.Execute(null);

        page.OpenProgramModalCommand.Execute(null);
        Assert.Equal(2, source.Pending.Count);
        source.Pending[1].SetResult([
            new(@"C:\Apps\Editor.exe", "Editor.exe", "Editor", now.AddHours(-1)),
            new(@"C:\Apps\Browser.exe", "Browser.exe", "Browser", now)
        ]);
        await modal.RefreshTask;
        Assert.Equal(new[] { "Browser", "Editor" }, modal.RecentPrograms.Select(program => program.DisplayName));
        modal.SearchText = "browser.exe";
        var latest = Assert.Single(modal.RecentPrograms);
        modal.SelectProgramCommand.Execute(latest);
        modal.SaveCommand.Execute(null);
        Assert.False(modal.IsOpen);
        Assert.Equal(@"C:\Apps\Browser.exe", Assert.Single(page.Applications).Path);
        Assert.True(page.Applications[0].IsEnabled);
    }

    [Fact]
    public async Task AClosedOrOlderRequestCannotOverwriteANewerOpen()
    {
        var source = new ControlledSource();
        var modal = new AddProgramModalViewModel(recentProgramSource: source);
        modal.Open();
        var oldRefresh = modal.RefreshTask;
        modal.CloseCommand.Execute(null);
        modal.Open();
        source.Pending[1].SetResult([new(@"C:\Apps\New.exe", "New.exe", "New", DateTime.Now)]);
        await modal.RefreshTask;
        source.Pending[0].SetResult([new(@"C:\Apps\Old.exe", "Old.exe", "Old", DateTime.Now.AddDays(-1))]);
        await oldRefresh;
        Assert.Equal("New", Assert.Single(modal.RecentPrograms).DisplayName);

        modal.CloseCommand.Execute(null);
        modal.Open();
        var closedRefresh = modal.RefreshTask;
        modal.CloseCommand.Execute(null);
        source.Pending[2].SetResult([]);
        await closedRefresh;
        Assert.Equal("New", Assert.Single(modal.RecentPrograms).DisplayName);
    }

    [Fact]
    public async Task ManualChoiceSurvivesAnInFlightScanAndDisappearsOnTheNextFreshScan()
    {
        var source = new ControlledSource();
        var modal = new AddProgramModalViewModel(recentProgramSource: source);
        modal.Open();
        modal.ApplyProgramSelection(new(@"C:\Apps\Chosen.exe", "Chosen.exe", "Chosen"));
        var selected = modal.SelectedProgram;
        source.Pending[0].SetResult([]);
        await modal.RefreshTask;
        Assert.Same(selected, modal.SelectedProgram);
        Assert.Same(selected, Assert.Single(modal.RecentPrograms));
        modal.CloseCommand.Execute(null);
        modal.Open();
        source.Pending[1].SetResult([]);
        await modal.RefreshTask;
        Assert.Null(modal.SelectedProgram);
        Assert.Empty(modal.RecentPrograms);
    }

    [Fact]
    public async Task RefreshPublishesOneBatchAndKeepsOnlyTheNewestDuplicate()
    {
        var source = new ControlledSource();
        var modal = new AddProgramModalViewModel(recentProgramSource: source);
        var batches = 0;
        modal.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(modal.RecentPrograms)) batches++; };
        modal.Open();
        var now = DateTime.Now;
        source.Pending[0].SetResult([
            new(@"C:\Apps\Editor.exe", "Editor.exe", "Editor", now.AddDays(-1)),
            new(@"c:\apps\EDITOR.exe", "EDITOR.exe", "Editor", now),
            new(@"C:\Windows\System32\svchost.exe", "svchost.exe", "Service", now)
        ]);
        await modal.RefreshTask;
        Assert.Equal(now, Assert.Single(modal.RecentPrograms).LastRunTime);
        Assert.Equal(1, batches);
    }

    private sealed class ControlledSource : IRecentProgramSource
    {
        public List<TaskCompletionSource<IReadOnlyList<RecentProgramRecord>>> Pending { get; } = [];
        public Task<IReadOnlyList<RecentProgramRecord>> GetRecentProgramsAsync(CancellationToken cancellationToken = default)
        {
            var request = new TaskCompletionSource<IReadOnlyList<RecentProgramRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(request);
            return request.Task;
        }
    }
}
