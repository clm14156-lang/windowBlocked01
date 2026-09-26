using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalViewModelTests
{
    [Fact]
    public void MonthlyDailyRequirementUsesTheDraftTargetCompletedTimeAndRemainingDays()
    {
        var completed = TimeSpan.FromHours(5);
        var now = new DateTime(2026, 9, 13);
        var viewModel = new FocusGoalSettingsModalViewModel(() => now, () => completed);
        viewModel.SelectMonthlyModeCommand.Execute(null);
        Assert.Equal(18, viewModel.RemainingDays);
        Assert.Equal("按当前进度，每天约需 3.1 小时", viewModel.MonthlyDailyRequirementDisplay);

        viewModel.MonthlyTargetHoursInput = "80";
        Assert.Equal("按当前进度，每天约需 4.2 小时", viewModel.MonthlyDailyRequirementDisplay);
        viewModel.IncreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal("按当前进度，每天约需 4.2 小时", viewModel.MonthlyDailyRequirementDisplay);
        completed = TimeSpan.FromHours(27);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        viewModel.RefreshMonthlyProgress();
        Assert.Equal("按当前进度，每天约需 3.0 小时", viewModel.MonthlyDailyRequirementDisplay);
        Assert.Contains(nameof(viewModel.MonthlyDailyRequirementDisplay), changedProperties);

        now = new DateTime(2026, 9, 30);
        viewModel.RefreshMonthlyProgress();
        Assert.Equal(1, viewModel.RemainingDays);
        Assert.Equal("按当前进度，每天约需 54.0 小时", viewModel.MonthlyDailyRequirementDisplay);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(60, 80)]
    [InlineData(0, 0)]
    public void CompletedOrExceededMonthlyTargetsNeverSuggestNegativeHours(int targetHours, int completedHours)
    {
        var viewModel = new FocusGoalSettingsModalViewModel(() => new DateTime(2026, 9, 30), () => TimeSpan.FromHours(completedHours))
        { MonthlyTargetHoursInput = targetHours.ToString() };
        Assert.Equal(0, viewModel.DailyRequiredFocusHours);
        Assert.Equal("按当前进度，每天约需 0.0 小时", viewModel.MonthlyDailyRequirementDisplay);
    }

    [Theory]
    [InlineData(2024, 2, 28, 2)]
    [InlineData(2025, 2, 28, 1)]
    [InlineData(2026, 9, 1, 30)]
    public void RemainingDaysIncludesTodayAndHandlesLeapYears(int year, int month, int day, int expectedDays)
    {
        var viewModel = new FocusGoalSettingsModalViewModel(() => new DateTime(year, month, day));
        Assert.Equal(expectedDays, viewModel.RemainingDays);
    }

    [Fact]
    public void NewModal_UsesDailyDefaultsAnd350DipHeight()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        Assert.False(viewModel.IsOpen);
        Assert.Equal(FocusGoalMode.DailyFixed, viewModel.Mode);
        Assert.Equal(4, viewModel.DailyTargetHours);
        Assert.Equal(60, viewModel.MonthlyTargetHours);
        Assert.Equal(350, viewModel.DialogHeight);
    }

    [Fact]
    public void OpenAndCancelCommandsOnlyToggleModalVisibility()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        viewModel.OpenCommand.Execute(null);
        Assert.True(viewModel.IsOpen);

        viewModel.CancelCommand.Execute(null);
        Assert.False(viewModel.IsOpen);

        viewModel.OpenCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void DeletingSavedTargetResetsTargetStateAndClosesModal()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();
        viewModel.SaveCommand.Execute(null);
        viewModel.OpenCommand.Execute(null);

        viewModel.ToggleMoreMenuCommand.Execute(null);

        Assert.True(viewModel.IsMoreMenuOpen);
        Assert.True(viewModel.HasSavedTarget);

        viewModel.DeleteTargetCommand.Execute(null);

        Assert.False(viewModel.HasSavedTarget);
        Assert.False(viewModel.HasSavedDailyFixedTarget);
        Assert.False(viewModel.IsMoreMenuOpen);
        Assert.False(viewModel.IsOpen);
        Assert.Equal(FocusGoalMode.DailyFixed, viewModel.Mode);
    }

    [Fact]
    public void MoreMenuDoesNotOpenWhenNoTargetHasBeenSaved()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        viewModel.ToggleMoreMenuCommand.Execute(null);

        Assert.False(viewModel.HasSavedTarget);
        Assert.False(viewModel.IsMoreMenuOpen);
    }

    [Fact]
    public void SelectingMonthlyModeUsesIndependentMonthlyTargetAnd410DipHeight()
    {
        var viewModel = new FocusGoalSettingsModalViewModel(() => new DateTime(2026, 9, 13), () => TimeSpan.FromHours(5));

        viewModel.SelectMonthlyModeCommand.Execute(null);

        Assert.Equal(FocusGoalMode.MonthlyTotal, viewModel.Mode);
        Assert.True(viewModel.IsMonthlyTotalMode);
        Assert.False(viewModel.IsDailyFixedMode);
        Assert.Equal(60, viewModel.MonthlyTargetHours);
        Assert.Equal(18, viewModel.RemainingDays);
        Assert.Equal(55, viewModel.RemainingHours);
        Assert.Equal(410, viewModel.DialogHeight);
    }

    [Fact]
    public void StepperCommandsAdjustTheActiveModeTargetWithoutGoingBelowOne()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        viewModel.DecreaseDailyTargetCommand.Execute(null);
        Assert.Equal(3, viewModel.DailyTargetHours);
        viewModel.IncreaseDailyTargetCommand.Execute(null);
        Assert.Equal(4, viewModel.DailyTargetHours);

        viewModel.DailyTargetHoursInput = "0";
        viewModel.DecreaseDailyTargetCommand.Execute(null);
        Assert.Equal(0, viewModel.DailyTargetHours);

        viewModel.DailyTargetHoursInput = "24";
        viewModel.IncreaseDailyTargetCommand.Execute(null);
        Assert.Equal(24, viewModel.DailyTargetHours);

        viewModel.SelectMonthlyModeCommand.Execute(null);
        viewModel.IncreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(61, viewModel.MonthlyTargetHours);
        viewModel.DecreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(60, viewModel.MonthlyTargetHours);

        viewModel.MonthlyTargetHoursInput = "720";
        viewModel.IncreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(720, viewModel.MonthlyTargetHours);
    }

    [Fact]
    public void DailyTargetInputAcceptsTwoDigitIntegersAndClampsAtTwentyFour()
    {
        var viewModel = new FocusGoalSettingsModalViewModel
        {
            DailyTargetHoursInput = "23"
        };

        viewModel.CommitDailyTargetHoursInput();

        Assert.Equal(23, viewModel.DailyTargetHours);
        Assert.Equal("23", viewModel.DailyTargetHoursInput);

        viewModel.DailyTargetHoursInput = "25";

        Assert.Equal(24, viewModel.DailyTargetHours);
        Assert.Equal("24", viewModel.DailyTargetHoursInput);

        viewModel.DailyTargetHoursInput = "99";

        Assert.Equal(24, viewModel.DailyTargetHours);
        Assert.Equal("24", viewModel.DailyTargetHoursInput);

        viewModel.DailyTargetHoursInput = "";
        viewModel.CommitDailyTargetHoursInput();

        Assert.Equal(24, viewModel.DailyTargetHours);
        Assert.Equal("24", viewModel.DailyTargetHoursInput);
    }

    [Fact]
    public void StepperUsesTheLatestManuallyEnteredValue()
    {
        var viewModel = new FocusGoalSettingsModalViewModel
        {
            DailyTargetHoursInput = "23"
        };

        viewModel.IncreaseDailyTargetCommand.Execute(null);

        Assert.Equal(24, viewModel.DailyTargetHours);
        Assert.Equal("24", viewModel.DailyTargetHoursInput);

        viewModel.IncreaseDailyTargetCommand.Execute(null);
        viewModel.DecreaseDailyTargetCommand.Execute(null);

        Assert.Equal(23, viewModel.DailyTargetHours);
        Assert.Equal("23", viewModel.DailyTargetHoursInput);
    }

    [Fact]
    public void MonthlyTargetUsesTheSameInputAndStepperSynchronization()
    {
        var viewModel = new FocusGoalSettingsModalViewModel
        {
            MonthlyTargetHoursInput = "80"
        };

        viewModel.IncreaseMonthlyTargetCommand.Execute(null);

        Assert.Equal(81, viewModel.MonthlyTargetHours);
        Assert.Equal("81", viewModel.MonthlyTargetHoursInput);

        viewModel.DecreaseMonthlyTargetCommand.Execute(null);

        Assert.Equal(80, viewModel.MonthlyTargetHours);
        Assert.Equal("80", viewModel.MonthlyTargetHoursInput);
    }
}
