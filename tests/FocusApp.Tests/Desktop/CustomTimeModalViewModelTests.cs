using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CustomTimeModalViewModelTests
{
    [Fact]
    public void QuickSelectionRestoresSavedChoicesAndEvictsByClickOrder()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", true, 60),
            new("90", "", true, 90), new("180", "", true, 180), new("45", "", false, 45)]);
        var modal = home.CustomTimeModal;
        var notifications = new List<string?>();
        modal.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        modal.Open();
        Assert.Equal([30, 60, 90, 180], modal.SelectedMinutes);
        var thirty = modal.CommonTimes.Single(option => option.Minutes == 30);
        var sixty = modal.CommonTimes.Single(option => option.Minutes == 60);
        modal.SelectTimeCommand.Execute(thirty);
        Assert.Equal("30", modal.MinutesInput);
        Assert.Equal([60, 90, 180], modal.SelectedMinutes);
        Assert.False(thirty.IsSelectedInCustomTime);
        modal.SelectTimeCommand.Execute(thirty);
        Assert.Equal([60, 90, 180, 30], modal.SelectedMinutes);
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 45));
        Assert.Equal([90, 180, 30, 45], modal.SelectedMinutes);
        Assert.False(sixty.IsSelectedInCustomTime);
        modal.SelectTimeCommand.Execute(sixty);
        Assert.Equal("60", modal.MinutesInput);
        Assert.Equal([180, 30, 45, 60], modal.SelectedMinutes);
        modal.SelectTimeCommand.Execute(sixty);
        Assert.Equal([180, 30, 45], modal.SelectedMinutes);
        modal.MinutesInput = "90";
        Assert.Equal([180, 30, 45], modal.SelectedMinutes);
        Assert.False(modal.CommonTimes.Single(option => option.Minutes == 90).IsSelectedInCustomTime);
        modal.MinutesInput = "";
        Assert.False(modal.ConfirmCommand.CanExecute(null));
        Assert.Contains(nameof(modal.SelectedMinutes), notifications);
    }

    [Fact]
    public void EditingDoesNotToggleChoicesAndDeletionRemovesTheDraftChoice()
    {
        var option = new HomeDurationOptionViewModel("60", "", true, 60);
        var modal = new CustomTimeModalViewModel(_ => true, [option]);
        modal.Open();
        modal.MinutesInput = "60";
        modal.ToggleEditCommand.Execute(null);
        modal.SelectTimeCommand.Execute(new HomeDurationOptionViewModel("90", "", false, 90));
        Assert.Equal(60, modal.Minutes);
        modal.DeleteTimeCommand.Execute(option);
        Assert.Empty(modal.SelectedMinutes);
        Assert.Empty(modal.CommonTimes);
        modal.CompleteEditCommand.Execute(null);
        modal.CommonTimes.Add(option);
        Assert.False(option.IsSelectedInCustomTime);
        modal.Open();
        Assert.Equal(5, modal.Minutes);
        Assert.Equal([60], modal.SelectedMinutes);
    }

    [Fact]
    public void AddCommitsExactOrderOnceAndCancelDiscardsUnconfirmedChoices()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60),
            new("90", "", false, 90), new("180", "", false, 180)]);
        var modal = home.CustomTimeModal;
        var writes = 0;
        home.DurationOptionsChanged += (_, _) => writes++;
        modal.Open();
        Click(180); Click(60); Click(30); Click(90);
        Assert.Equal([180, 60, 90], modal.SelectedMinutes);
        Assert.Equal([30], HomeMinutes());
        Assert.Equal(0, writes);
        modal.ConfirmCommand.Execute(null);
        Assert.Equal([180, 60, 90], HomeMinutes());
        Assert.Equal(1, writes);
        Click(180); Click(30);
        modal.CancelCommand.Execute(null);
        Assert.Equal([180, 60, 90], HomeMinutes());
        modal.Open();
        Assert.Equal([180, 60, 90], modal.SelectedMinutes);
        Assert.Equal(1, writes);
        Click(60); Click(60);
        modal.ConfirmCommand.Execute(null);
        Assert.Equal([180, 90, 60], HomeMinutes());
        modal.Open();
        Assert.Equal([180, 90, 60], modal.SelectedMinutes);
        void Click(int minutes) => modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == minutes));
        IEnumerable<int> HomeMinutes() => home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes);
    }

    [Fact]
    public void AddingCustomTimeUsesTheSameFourChoiceQueueAndExistingTimeCanSaveAtCapacity()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", true, 60),
            new("90", "", true, 90), new("180", "", true, 180)]);
        var modal = home.CustomTimeModal;
        modal.Open();
        modal.Minutes = 45;
        modal.ConfirmCommand.Execute(null);
        Assert.Equal([60, 90, 180, 45], modal.SelectedMinutes);
        Assert.Equal([60, 90, 180, 45], home.VisibleDurationOptions.Take(4).Select(option => option.Minutes));
        foreach (var minutes in new[] { 5, 10, 15, 20 })
        {
            modal.Minutes = minutes;
            modal.ConfirmCommand.Execute(null);
        }
        Assert.Equal(9, modal.CommonTimes.Count);
        modal.Minutes = 25;
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(25, modal.Minutes);
        Assert.Equal([5, 10, 15, 20], modal.SelectedMinutes);
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 180));
        modal.ConfirmCommand.Execute(null);
        Assert.Equal([10, 15, 20, 180], home.VisibleDurationOptions.Take(4).Select(option => option.Minutes));
    }

    [Fact]
    public void AllChoicesCanBeClearedAndAServiceRefreshPreservesAnUnsavedDraft()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60)]);
        var modal = home.CustomTimeModal;
        modal.Open();
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 60));
        home.ApplyDurationPresets(home.GetDurationPresets());
        Assert.Equal([30, 60], modal.SelectedMinutes);
        Assert.All(modal.CommonTimes, option => Assert.True(option.IsSelectedInCustomTime));
        foreach (var option in modal.CommonTimes) modal.SelectTimeCommand.Execute(option);
        modal.ConfirmCommand.Execute(null);
        Assert.Empty(home.VisibleDurationOptions.Where(option => option.Icon.Length == 0));
        var restarted = new HomePageViewModel([new("30", "", true, 30)]);
        restarted.ApplyDurationPresets(home.GetDurationPresets());
        restarted.CustomTimeModal.Open();
        Assert.Empty(restarted.CustomTimeModal.SelectedMinutes);
        Assert.Single(restarted.VisibleDurationOptions);
    }
}
