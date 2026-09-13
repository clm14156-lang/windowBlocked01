using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalViewModelTests
{
    [Fact]
    public void NewModal_UsesDailyEveryDayDefaultsAnd390DipHeight()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        Assert.False(viewModel.IsOpen);
        Assert.Equal(FocusGoalMode.DailyFixed, viewModel.Mode);
        Assert.Equal(FocusGoalRepeatMode.EveryDay, viewModel.RepeatMode);
        Assert.Equal(4, viewModel.DailyTargetHours);
        Assert.Equal(60, viewModel.MonthlyTargetHours);
        Assert.Equal(390, viewModel.DialogHeight);
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
    public void SelectingCustomRepeatShowsWeekdaysAndAllowsMultiSelect()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        viewModel.SelectCustomRepeatCommand.Execute(null);

        Assert.Equal(FocusGoalRepeatMode.Custom, viewModel.RepeatMode);
        Assert.Equal(430, viewModel.DialogHeight);
        Assert.Equal(5, viewModel.Weekdays.Count(weekday => weekday.IsSelected));

        var saturday = viewModel.Weekdays[5];
        viewModel.ToggleWeekdayCommand.Execute(saturday);

        Assert.True(saturday.IsSelected);
        Assert.Equal(6, viewModel.Weekdays.Count(weekday => weekday.IsSelected));
    }

    [Fact]
    public void SelectingEveryDayHidesWeekdaysAndRestores390DipHeight()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();
        viewModel.SelectCustomRepeatCommand.Execute(null);

        viewModel.SelectEveryDayCommand.Execute(null);

        Assert.Equal(FocusGoalRepeatMode.EveryDay, viewModel.RepeatMode);
        Assert.Equal(390, viewModel.DialogHeight);
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
        Assert.Equal(390, viewModel.DialogHeight);
    }

    [Fact]
    public void StepperCommandsAdjustTheActiveModeTargetWithoutGoingBelowOne()
    {
        var viewModel = new FocusGoalSettingsModalViewModel();

        viewModel.DecreaseDailyTargetCommand.Execute(null);
        Assert.Equal(3, viewModel.DailyTargetHours);
        viewModel.IncreaseDailyTargetCommand.Execute(null);
        Assert.Equal(4, viewModel.DailyTargetHours);

        viewModel.SelectMonthlyModeCommand.Execute(null);
        viewModel.IncreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(61, viewModel.MonthlyTargetHours);
        viewModel.DecreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(60, viewModel.MonthlyTargetHours);
    }
}
