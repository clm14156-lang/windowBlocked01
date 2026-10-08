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
        Assert.Equal("0", modal.MinutesInput);
        Assert.Equal([60, 90, 180], modal.SelectedMinutes);
        Assert.False(thirty.IsSelectedInCustomTime);
        modal.SelectTimeCommand.Execute(thirty);
        Assert.Equal([60, 90, 180, 30], modal.SelectedMinutes);
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 45));
        Assert.Equal([90, 180, 30, 45], modal.SelectedMinutes);
        Assert.False(sixty.IsSelectedInCustomTime);
        modal.SelectTimeCommand.Execute(sixty);
        Assert.Equal("0", modal.MinutesInput);
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
        Assert.Equal(0, modal.Minutes);
        Assert.Equal([60], modal.SelectedMinutes);
    }

    [Fact]
    public void EachSelectionSavesImmediatelyAndClosingPreservesTheExactClickOrder()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60),
            new("90", "", false, 90), new("180", "", false, 180)]);
        var modal = home.CustomTimeModal;
        var writes = 0;
        home.DurationOptionsChanged += (_, _) => writes++;
        modal.Open();
        Click(180); Click(60); Click(30); Click(90);
        Assert.Equal([180, 60, 90], modal.SelectedMinutes);
        Assert.Equal([60, 90, 180], HomeMinutes());
        Assert.Equal(4, writes);
        Assert.False(modal.ConfirmCommand.CanExecute(null));
        Click(180); Click(30);
        modal.CancelCommand.Execute(null);
        Assert.Equal([30, 60, 90], HomeMinutes());
        modal.Open();
        Assert.Equal([60, 90, 30], modal.SelectedMinutes);
        Assert.Equal(6, writes);
        Click(60); Click(60);
        Assert.Equal([30, 60, 90], HomeMinutes());
        modal.Open();
        Assert.Equal([90, 30, 60], modal.SelectedMinutes);
        Assert.Equal(8, writes);
        void Click(int minutes) => modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == minutes));
        IEnumerable<int> HomeMinutes() => home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes);
    }

    [Fact]
    public void NumericDisplayOrderStaysStableWhileFifoSelectionSurvivesRefreshAndRestart()
    {
        var home = new HomePageViewModel(new[] { 455, 180, 54, 60, 90, 30, 16, 5 }
            .Select(minutes => new HomeDurationOptionViewModel(minutes.ToString(), "", minutes == 30, minutes)));
        var modal = home.CustomTimeModal;
        modal.Open();
        Assert.Equal(new[] { 5, 16, 30, 54, 60, 90, 180, 455 }, CommonMinutes());
        var originalBlocks = modal.CommonTimes.ToArray();
        var collectionChanges = 0;
        modal.CommonTimes.CollectionChanged += (_, _) => collectionChanges++;
        Click(30); // Clear the initial shortcut.
        modal.MinutesInput = "50";
        foreach (var minutes in new[] { 180, 60, 16, 5 }) Click(minutes);
        Assert.Equal(new[] { 5, 16, 60, 180 }, HomeMinutes());
        Assert.Equal(new[] { 180, 60, 16, 5 }, modal.SelectedMinutes);
        Click(54);
        Assert.Equal(new[] { 60, 16, 5, 54 }, modal.SelectedMinutes);
        Assert.Equal(new[] { 5, 16, 54, 60 }, HomeMinutes());
        Click(16); Click(16);
        Assert.Equal(new[] { 60, 5, 54, 16 }, modal.SelectedMinutes);
        Assert.Equal(originalBlocks, modal.CommonTimes);
        Assert.Equal(0, collectionChanges);
        Assert.Equal("50", modal.MinutesInput);
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(new[] { 5, 16, 30, 50, 54, 60, 90, 180, 455 }, CommonMinutes());
        Assert.Equal(new[] { 5, 16, 54, 60 }, HomeMinutes());
        Assert.Equal("0", modal.MinutesInput);
        home.ApplyDurationPresets(home.GetDurationPresets());
        Assert.Equal(new[] { 5, 16, 30, 50, 54, 60, 90, 180, 455 }, CommonMinutes());
        modal.CancelCommand.Execute(null); modal.Open();
        Assert.Equal(new[] { 60, 5, 54, 16 }, modal.SelectedMinutes);
        var restarted = new HomePageViewModel([new("30", "", true, 30)]);
        restarted.ApplyDurationPresets(home.GetDurationPresets());
        restarted.CustomTimeModal.Open();
        Assert.Equal(new[] { 5, 16, 54, 60 }, restarted.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
        Assert.Equal(CommonMinutes(), restarted.CustomTimeModal.CommonTimes.Select(option => option.Minutes));
        restarted.CustomTimeModal.SelectTimeCommand.Execute(restarted.CustomTimeModal.CommonTimes.Single(option => option.Minutes == 180));
        Assert.Equal(new[] { 5, 54, 16, 180 }, restarted.CustomTimeModal.SelectedMinutes);
        Assert.Equal(new[] { 5, 16, 54, 180 }, restarted.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
        void Click(int minutes) => modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == minutes));
        IEnumerable<int> HomeMinutes() => home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes);
        IEnumerable<int> CommonMinutes() => modal.CommonTimes.Select(option => option.Minutes);
    }

    [Fact]
    public void AddingNewTimesDoesNotSelectThemAndTheNinePresetCapacityIsUnchanged()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", true, 60),
            new("90", "", true, 90), new("180", "", true, 180)]);
        var modal = home.CustomTimeModal;
        modal.Open();
        modal.Minutes = 45;
        modal.ConfirmCommand.Execute(null);
        Assert.Equal([30, 60, 90, 180], modal.SelectedMinutes);
        Assert.Equal([30, 60, 90, 180], home.VisibleDurationOptions.Take(4).Select(option => option.Minutes));
        Assert.Equal(0, modal.Minutes);
        Assert.False(modal.CommonTimes.Single(option => option.Minutes == 45).IsSelectedInCustomTime);
        foreach (var minutes in new[] { 5, 10, 15, 20 })
        {
            modal.Minutes = minutes;
            modal.ConfirmCommand.Execute(null);
        }
        Assert.Equal(9, modal.CommonTimes.Count);
        modal.Minutes = 25;
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(25, modal.Minutes);
        Assert.Equal([30, 60, 90, 180], modal.SelectedMinutes);
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 180));
        Assert.Equal(25, modal.Minutes);
        Assert.Equal([30, 60, 90], home.VisibleDurationOptions.Take(3).Select(option => option.Minutes));
        modal.Minutes = 180;
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(0, modal.Minutes);
        Assert.Equal(9, modal.CommonTimes.Count);
        Assert.Equal([30, 60, 90], modal.SelectedMinutes);
    }

    [Fact]
    public void AllChoicesCanBeClearedAndServiceRefreshPreservesTheImmediatelySavedSelection()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60)]);
        var modal = home.CustomTimeModal;
        modal.Open();
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 60));
        home.ApplyDurationPresets(home.GetDurationPresets());
        Assert.Equal([30, 60], modal.SelectedMinutes);
        Assert.All(modal.CommonTimes, option => Assert.True(option.IsSelectedInCustomTime));
        foreach (var option in modal.CommonTimes) modal.SelectTimeCommand.Execute(option);
        Assert.Empty(home.VisibleDurationOptions.Where(option => option.Icon.Length == 0));
        var restarted = new HomePageViewModel([new("30", "", true, 30)]);
        restarted.ApplyDurationPresets(home.GetDurationPresets());
        restarted.CustomTimeModal.Open();
        Assert.Empty(restarted.CustomTimeModal.SelectedMinutes);
        Assert.Single(restarted.VisibleDurationOptions);
    }

    [Fact]
    public void AddAndSelectionAreIndependentAndDuplicateAddsNeverCreateOrSelectAnotherPreset()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60)]);
        var modal = home.CustomTimeModal;
        var writes = 0;
        home.DurationOptionsChanged += (_, _) => writes++;
        modal.Open();
        Assert.Equal("0", modal.MinutesInput);
        Assert.False(modal.ConfirmCommand.CanExecute(null));
        modal.MinutesInput = "45";
        modal.SelectTimeCommand.Execute(modal.CommonTimes.Single(option => option.Minutes == 60));
        Assert.Equal("45", modal.MinutesInput);
        modal.ConfirmCommand.Execute(null);
        Assert.Equal("0", modal.MinutesInput);
        var added = Assert.Single(modal.CommonTimes.Where(option => option.Minutes == 45));
        Assert.False(added.IsSelectedInCustomTime);
        Assert.False(added.IsSelected);
        Assert.Equal([30, 60], modal.SelectedMinutes);
        Assert.Equal(2, writes); // One immediate selection and one newly persisted preset.
        var beforeDuplicate = home.GetDurationPresets();
        modal.Minutes = 45;
        modal.ConfirmCommand.Execute(null);
        Assert.Equal("0", modal.MinutesInput);
        Assert.Equal(beforeDuplicate, home.GetDurationPresets());
        Assert.Equal(2, writes);
        Assert.False(home.FocusSession.IsFocusing);
        modal.MinutesInput = "480";
        modal.CancelCommand.Execute(null);
        modal.Open();
        Assert.Equal("0", modal.MinutesInput);
        Assert.Equal([30, 60], modal.SelectedMinutes);
    }

    [Theory]
    [InlineData("", false)] [InlineData("0", false)] [InlineData("4", false)]
    [InlineData("5", true)] [InlineData("480", true)] [InlineData("481", false)]
    [InlineData("30.5", false)] [InlineData("-30", false)] [InlineData("+30", false)]
    [InlineData(" 30", false)] [InlineData("abc", false)] [InlineData("2147483648", false)]
    public void AddAcceptsOnlyWholeMinutesWithinTheSupportedRange(string input, bool valid)
    {
        var calls = 0;
        var modal = new CustomTimeModalViewModel(_ => { calls++; return true; });
        modal.Open();
        modal.MinutesInput = input;
        Assert.Equal(valid, modal.ConfirmCommand.CanExecute(null));
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(valid ? 1 : 0, calls);
        Assert.Equal(valid ? "0" : input, modal.MinutesInput);
        Assert.Empty(modal.SelectedMinutes);
    }
}
