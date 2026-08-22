using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public void ToggleItem_UpdatesOnlyInMemoryState()
    {
        var item = new SettingsToggleItemViewModel("FloatingWindow", "Floating window", "Description", "Icon", false);
        var viewModel = new SettingsPageViewModel([item], []);

        item.IsEnabled = true;

        Assert.True(viewModel.ToggleItems[0].IsEnabled);
    }

    [Fact]
    public void IsFloatingWindowEnabled_ReflectsCurrentToggleState()
    {
        var item = new SettingsToggleItemViewModel("FloatingWindow", "Floating window", "Description", "Icon", false);
        var viewModel = new SettingsPageViewModel([item], []);

        Assert.False(viewModel.IsFloatingWindowEnabled);

        item.IsEnabled = true;

        Assert.True(viewModel.IsFloatingWindowEnabled);
    }

    [Fact]
    public void ActivateEntryCommand_TracksLatestClickedEntry()
    {
        var export = new SettingsEntryItemViewModel("Export", "Export", "Description", "Icon");
        var about = new SettingsEntryItemViewModel("About", "About", "Description", "Icon", false);
        var viewModel = new SettingsPageViewModel([], [export, about]);

        viewModel.ActivateEntryCommand.Execute(export);
        viewModel.ActivateEntryCommand.Execute(about);

        Assert.False(export.IsActive);
        Assert.True(about.IsActive);
        Assert.Equal("About", viewModel.LastActivatedEntryKey);
    }

    [Fact]
    public void RuleModal_OpenAndCustomMode_ResetExpectedDefaults()
    {
        var modal = CreateRuleModal();

        modal.Open();
        modal.SelectCustomCommand.Execute(null);

        Assert.True(modal.IsOpen);
        Assert.True(modal.IsCustom);
        Assert.Equal("周一、周三、周五", modal.SelectedDaysText);
        Assert.Equal("09:00", modal.StartTimeText);
        Assert.Equal("12:00", modal.EndTimeText);
    }

    [Fact]
    public void TimeInputsAndSliderValues_StaySynchronizedAndOrdered()
    {
        var modal = CreateRuleModal();
        modal.Open();

        modal.StartTimeText = "10:30";
        Assert.Equal(630, modal.StartValue);

        modal.StartValue = 8 * 60;
        Assert.Equal("08:00", modal.StartTimeText);

        modal.EndTimeText = "07:00";
        Assert.Equal(modal.StartValue, modal.EndValue);
        Assert.Equal("08:00", modal.EndTimeText);
    }

    [Fact]
    public void TimePicker_ConfirmsFiveMinuteSelectionAndWheelStaysSynchronized()
    {
        var modal = CreateRuleModal();
        modal.Open();

        modal.OpenTimePicker(true);
        Assert.True(modal.IsStartTimePickerOpen);
        Assert.Equal(9, modal.SelectedHour?.Value);
        Assert.Equal(0, modal.SelectedMinute?.Value);
        Assert.Equal(5, modal.HourWheelItems.Count);
        Assert.Equal(5, modal.MinuteWheelItems.Count);
        Assert.Equal(9, Assert.Single(modal.HourWheelItems.Where(item => item.IsSelected)).Value);
        Assert.Equal(0, Assert.Single(modal.MinuteWheelItems.Where(item => item.IsSelected)).Value);

        modal.AdjustPickerWheel(true, -120);
        Assert.Equal(10, modal.SelectedHour?.Value);
        Assert.Equal(10, Assert.Single(modal.HourWheelItems.Where(item => item.IsSelected)).Value);

        var nextMinute = modal.MinuteWheelItems.Single(item => item.Value == 5);
        modal.SelectMinuteWheelItemCommand.Execute(nextMinute);
        Assert.Equal(5, modal.SelectedMinute?.Value);
        Assert.Equal(5, Assert.Single(modal.MinuteWheelItems.Where(item => item.IsSelected)).Value);

        modal.SelectedHour = modal.HourOptions[9];
        modal.SelectedMinute = modal.MinuteOptions.Single(option => option.Value == 30);
        modal.ConfirmTimePickerCommand.Execute(null);

        Assert.False(modal.IsTimePickerOpen);
        Assert.Equal(570, modal.StartValue);
        Assert.Equal("09:30", modal.StartTimeText);

        modal.AdjustTimeByWheel(true, 120);
        Assert.Equal(575, modal.StartValue);
        Assert.Equal("09:35", modal.StartTimeText);

        modal.StartValue = 10 * 60;
        Assert.Equal("10:00", modal.StartTimeText);

        modal.OpenTimePicker(true);
        modal.StartValue = 10 * 60 + 3;
        Assert.Equal(10 * 60 + 5, modal.StartValue);
        Assert.Equal("10:05", modal.StartTimeText);
        Assert.Equal(10, modal.SelectedHour?.Value);
        Assert.Equal(5, modal.SelectedMinute?.Value);
    }

    [Fact]
    public void TimePicker_ClearReservesSafeSliderBoundaryUntilTimeIsSelectedAgain()
    {
        var modal = CreateRuleModal();
        modal.Open();

        modal.OpenTimePicker(false);
        modal.ClearTimePickerCommand.Execute(null);

        Assert.False(modal.IsTimePickerOpen);
        Assert.Equal("--:--", modal.EndTimeText);
        Assert.Equal(24 * 60, modal.EndValue);

        modal.ConfirmCommand.Execute(null);
        Assert.True(modal.IsOpen);
        Assert.Equal("请选择开始时间和结束时间", modal.ValidationMessage);

        modal.AdjustTimeByWheel(false, -120);
        Assert.Equal("23:55", modal.EndTimeText);
        Assert.Equal(23 * 60 + 55, modal.EndValue);
    }

    [Fact]
    public void AddAndDeleteRule_UpdatesOnlyRequestedRule()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var forced = new SettingsToggleItemViewModel("ForcedMode", "Forced", "Description", "Icon", false, false);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic, forced], [], modal, "每天");

        modal.Open();
        modal.ConfirmCommand.Execute(null);
        modal.Open();
        modal.SelectCustomCommand.Execute(null);
        modal.EndTimeText = "18:00";
        modal.StartTimeText = "14:00";
        modal.ConfirmCommand.Execute(null);

        Assert.Equal(2, viewModel.AutomaticRules.Count);
        Assert.Equal("每天", viewModel.AutomaticRules[0].RepeatText);
        Assert.Equal("周一 / 周三 / 周五", viewModel.AutomaticRules[1].RepeatText);
        Assert.Equal("14:00 – 18:00", viewModel.AutomaticRules[1].TimeRangeText);

        viewModel.DeleteRuleCommand.Execute(viewModel.AutomaticRules[0]);

        Assert.Single(viewModel.AutomaticRules);
        Assert.Equal("周一 / 周三 / 周五", viewModel.AutomaticRules[0].RepeatText);
    }

    [Fact]
    public void RuleValidation_RejectsDuplicateAndCoveredIntervalsButAllowsPartialOverlap()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");

        modal.Open();
        modal.ConfirmCommand.Execute(null);
        Assert.Single(viewModel.AutomaticRules);

        modal.Open();
        modal.ConfirmCommand.Execute(null);
        Assert.Equal("已存在相同的自动屏蔽规则", modal.ValidationMessage);
        Assert.Single(viewModel.AutomaticRules);

        modal.Open();
        modal.EndTimeText = "10:00";
        modal.ConfirmCommand.Execute(null);
        Assert.Equal("该时间段已被现有规则覆盖", modal.ValidationMessage);
        Assert.Single(viewModel.AutomaticRules);

        modal.Open();
        modal.StartTimeText = "10:00";
        modal.EndTimeText = "14:00";
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(2, viewModel.AutomaticRules.Count);
    }

    [Fact]
    public void HomeRulePreview_SelectsNearestEnabledOccurrenceAndUpdatesAfterDelete()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var monday = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "22:00 – 00:00", ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 1320, 1440);
        var earlier = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "09:00 – 10:00", ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 540, 600);

        home.UpdateAutomaticRules([monday, earlier], new DateTime(2026, 8, 17, 12, 0, 0));

        Assert.True(home.HasNextAutomaticRule);
        Assert.Equal("自动屏蔽 · 22:00", home.NextAutomaticBlockingDisplay);
        Assert.Contains("持续 2 小时", home.NextAutomaticBlockingToolTip);

        monday.IsEnabled = false;
        home.UpdateAutomaticRules([monday, earlier], new DateTime(2026, 8, 17, 12, 0, 0));
        Assert.False(home.HasNextAutomaticRule);
    }

    [Fact]
    public void HomeRulePreview_HidesWhenGlobalAutomaticBlockingIsDisabled()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var rule = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "09:00 – 10:00",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 540, 600);
        var now = new DateTime(2026, 8, 17, 8, 0, 0);

        home.UpdateAutomaticRules([rule], false, now);
        Assert.False(home.HasNextAutomaticRule);
        Assert.Empty(home.NextAutomaticStartDisplay);

        home.UpdateAutomaticRules([rule], true, now);
        Assert.True(home.HasNextAutomaticRule);
        Assert.Equal("09:00", home.NextAutomaticStartDisplay);
    }

    [Fact]
    public void AutomaticBlocking_StartsFocusOnceForAnActiveRuleAndIgnoresOverlap()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var first = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "09:00 – 11:00",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 540, 660);
        var overlap = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "10:00 – 12:00",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 600, 720);
        var now = new DateTime(2026, 8, 17, 10, 30, 0);

        home.EvaluateAutomaticBlocking([first, overlap], true, now);
        home.EvaluateAutomaticBlocking([first, overlap], true, now.AddSeconds(1));

        Assert.True(home.FocusSession.IsPreparing);
        home.FocusSession.CancelPreparationCommand.Execute(null);
        home.EvaluateAutomaticBlocking([first, overlap], true, now.AddMinutes(1));
        Assert.False(home.FocusSession.IsActive);
    }

    [Fact]
    public void AutomaticBlocking_StartsWhenApplicationOpensInsideRuleWindow()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var rule = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "09:00 – 10:00",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 540, 600);

        home.EvaluateAutomaticBlocking(
            [rule], true, new DateTime(2026, 8, 17, 9, 30, 0));

        Assert.True(home.FocusSession.IsPreparing);
        home.FocusSession.CancelPreparationCommand.Execute(null);
    }

    [Fact]
    public void AutomaticBlocking_HidesCompletedRuleForTheRestOfTheDay()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var rule = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "08:41 – 08:42",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 521, 522);
        var beforeStart = new DateTime(2026, 8, 17, 8, 40, 0);

        home.UpdateAutomaticRules([rule], true, beforeStart);
        Assert.Equal("08:41", home.NextAutomaticStartDisplay);

        home.EvaluateAutomaticBlocking([rule], true, beforeStart.AddMinutes(1));
        Assert.False(home.HasNextAutomaticRule);
        home.FocusSession.CancelPreparationCommand.Execute(null);
        home.EvaluateAutomaticBlocking([rule], true, beforeStart.AddMinutes(2));

        Assert.False(home.HasNextAutomaticRule);

        home.UpdateAutomaticRules([rule], true, beforeStart.Date.AddDays(1).AddHours(8).AddMinutes(40));
        Assert.Equal("08:41", home.NextAutomaticStartDisplay);
    }

    private static AutomaticRuleModalViewModel CreateRuleModal()
    {
        return new AutomaticRuleModalViewModel(
        [
            new("Monday", "周一", "一", true),
            new("Tuesday", "周二", "二", false),
            new("Wednesday", "周三", "三", true),
            new("Thursday", "周四", "四", false),
            new("Friday", "周五", "五", true),
            new("Saturday", "周六", "六", false),
            new("Sunday", "周日", "日", false)
        ]);
    }
}
