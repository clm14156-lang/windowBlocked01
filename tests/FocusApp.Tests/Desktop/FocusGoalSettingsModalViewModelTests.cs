using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalViewModelTests
{
    [Theory]
    [InlineData("999")]
    [InlineData("10000")]
    [InlineData("999999999999999999999999999999")]
    public void OversizedMonthlyInputAndStepperClampToTheSameDynamicLimit(string input)
    {
        var viewModel = new FocusGoalSettingsModalViewModel(
            () => new DateTime(2026, 10, 22), () => TimeSpan.FromHours(10));
        viewModel.SelectMonthlyModeCommand.Execute(null);
        viewModel.MonthlyTargetHoursInput = input;

        Assert.Equal(10, viewModel.RemainingDays);
        Assert.Equal(250, viewModel.MonthlyTargetMaximumHours);
        Assert.Equal(250, viewModel.MonthlyTargetHours);
        Assert.Equal("250", viewModel.MonthlyTargetHoursInput);
        Assert.Equal(24, viewModel.DailyRequiredFocusHours);
        viewModel.IncreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(250, viewModel.MonthlyTargetHours);

        int? savedTarget = null;
        viewModel.GoalSettingsChanged += (_, _) => savedTarget = viewModel.MonthlyTargetHours;
        viewModel.SaveCommand.Execute(null);
        Assert.Equal(250, savedTarget);
        viewModel.MonthlyTargetHoursInput = "0";
        viewModel.DecreaseMonthlyTargetCommand.Execute(null);
        Assert.Equal(0, viewModel.MonthlyTargetHours);
        Assert.Equal(0, viewModel.DailyRequiredFocusHours);
    }

    [Theory]
    [InlineData(2026, 10, 1, 0, 744)]
    [InlineData(2026, 9, 1, 0, 720)]
    [InlineData(2024, 2, 1, 0, 696)]
    [InlineData(2025, 2, 1, 0, 672)]
    [InlineData(2026, 10, 31, 10, 34)]
    [InlineData(2026, 10, 22, 10.5, 250)]
    public void MonthlyLimitUsesActualMonthLengthAndRoundsFractionalHoursDown(
        int year, int month, int day, double completedHours, int expectedMaximum)
    {
        var viewModel = new FocusGoalSettingsModalViewModel(
            () => new DateTime(year, month, day), () => TimeSpan.FromHours(completedHours));
        viewModel.MonthlyTargetHoursInput = "999";

        Assert.Equal(expectedMaximum, viewModel.MonthlyTargetMaximumHours);
        Assert.Equal(expectedMaximum, viewModel.MonthlyTargetHours);
        Assert.InRange(viewModel.DailyRequiredFocusHours, 0, 24);
        Assert.Equal((expectedMaximum - completedHours) / viewModel.RemainingDays, viewModel.DailyRequiredFocusHours, 10);
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("commit-empty")]
    public void DateChangesRevalidateDraftIncludingImmediatelyBeforeSaving(string action)
    {
        var now = new DateTime(2026, 10, 22);
        var viewModel = new FocusGoalSettingsModalViewModel(() => now, () => TimeSpan.FromHours(10));
        viewModel.SelectMonthlyModeCommand.Execute(null);
        viewModel.OpenCommand.Execute(null);
        viewModel.MonthlyTargetHoursInput = "250";
        now = now.AddDays(1);
        Assert.Equal(226, viewModel.MonthlyTargetMaximumHours);
        Assert.InRange(viewModel.DailyRequiredFocusHours, 0, 24);
        int? savedTarget = null;
        viewModel.GoalSettingsChanged += (_, _) => savedTarget = viewModel.MonthlyTargetHours;

        switch (action)
        {
            case "refresh": viewModel.RefreshMonthlyProgress(); break;
            case "open": viewModel.CancelCommand.Execute(null); viewModel.OpenCommand.Execute(null); break;
            case "save": viewModel.SaveCommand.Execute(null); Assert.Equal(226, savedTarget); break;
            case "commit-empty": viewModel.MonthlyTargetHoursInput = ""; viewModel.CommitTargetHoursInput(true); break;
        }

        Assert.Equal(226, viewModel.MonthlyTargetHours);
        Assert.Equal("226", viewModel.MonthlyTargetHoursInput);
        Assert.Equal(24, viewModel.DailyRequiredFocusHours);
    }

    [Fact]
    public void NewMonthRecalculatesLimitAndCompletedFocusWithoutKeepingThePreviousMonthsCap()
    {
        var now = new DateTime(2026, 10, 31);
        var completed = TimeSpan.FromHours(10);
        var viewModel = new FocusGoalSettingsModalViewModel(() => now, () => completed);
        viewModel.ApplyPersistedMonthlyTarget(720);
        viewModel.OpenCommand.Execute(null);
        Assert.Equal(34, viewModel.MonthlyTargetHours);

        now = new DateTime(2026, 11, 1);
        completed = TimeSpan.Zero;
        viewModel.RefreshMonthlyProgress();
        Assert.Equal(30, viewModel.RemainingDays);
        Assert.Equal(720, viewModel.MonthlyTargetMaximumHours);
        viewModel.MonthlyTargetHoursInput = "999";
        Assert.Equal(720, viewModel.MonthlyTargetHours);
        Assert.Equal(24, viewModel.DailyRequiredFocusHours);
    }

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
        Assert.Equal(51, viewModel.MonthlyTargetHours);
        Assert.Equal("51", viewModel.MonthlyTargetHoursInput);
        Assert.Equal("按当前进度，每天约需 24.0 小时", viewModel.MonthlyDailyRequirementDisplay);
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
        var viewModel = new FocusGoalSettingsModalViewModel(() => new DateTime(2026, 10, 7));

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
        Assert.Equal(600, viewModel.MonthlyTargetHours);
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
        var viewModel = new FocusGoalSettingsModalViewModel(() => new DateTime(2026, 10, 7))
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
