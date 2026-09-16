using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalViewModelTests
{
    [Fact]
    public void NewModal_UsesDailyEveryDayDefaultsAnd420DipHeight()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        Assert.False(viewModel.IsOpen);
        Assert.Equal(FocusGoalMode.DailyFixed, viewModel.Mode);
        Assert.Equal(FocusGoalRepeatMode.EveryDay, viewModel.RepeatMode);
        Assert.Equal(4, viewModel.DailyTargetHours);
        Assert.Equal(60, viewModel.MonthlyTargetHours);
        Assert.Equal(420, viewModel.DialogHeight);
        Assert.Equal(7, viewModel.Weekdays.Count);
        Assert.All(viewModel.Weekdays.Take(5), weekday => Assert.True(weekday.IsSelected));
        Assert.All(viewModel.Weekdays.Skip(5), weekday => Assert.False(weekday.IsSelected));
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
        Assert.Equal(FocusGoalRepeatMode.EveryDay, viewModel.RepeatMode);
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
    public void SelectingCustomRepeatShowsWeekdaysAndAllowsMultiSelect()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        viewModel.SelectCustomRepeatCommand.Execute(null);

        Assert.Equal(FocusGoalRepeatMode.Custom, viewModel.RepeatMode);
        Assert.Equal(460, viewModel.DialogHeight);
        Assert.Equal(5, viewModel.Weekdays.Count(weekday => weekday.IsSelected));

        var saturday = viewModel.Weekdays[5];
        viewModel.ToggleWeekdayCommand.Execute(saturday);

        Assert.True(saturday.IsSelected);
        Assert.Equal(6, viewModel.Weekdays.Count(weekday => weekday.IsSelected));
    }

    [Fact]
    public void SelectingEveryDayHidesWeekdaysAndRestores420DipHeight()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();
        viewModel.SelectCustomRepeatCommand.Execute(null);

        viewModel.SelectEveryDayCommand.Execute(null);

        Assert.Equal(FocusGoalRepeatMode.EveryDay, viewModel.RepeatMode);
        Assert.Equal(420, viewModel.DialogHeight);
    }

    [Fact]
    public void SelectingMonthlyModeUsesIndependentMonthlyTargetAndHidesRepeatState()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();
        viewModel.SelectCustomRepeatCommand.Execute(null);

        viewModel.SelectMonthlyModeCommand.Execute(null);

        Assert.Equal(FocusGoalMode.MonthlyTotal, viewModel.Mode);
        Assert.True(viewModel.IsMonthlyTotalMode);
        Assert.False(viewModel.IsDailyFixedMode);
        Assert.Equal(60, viewModel.MonthlyTargetHours);
        Assert.Equal(18, viewModel.RemainingDays);
        Assert.Equal(55, viewModel.RemainingHours);
        Assert.Equal(420, viewModel.DialogHeight);
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
