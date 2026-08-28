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
    public void ForcedMode_RequiresLoggedInVipAccessAndStaysOffWithoutIt()
    {
        var forcedMode = new SettingsToggleItemViewModel("ForcedMode", "Forced mode", "Description", "Icon", true, false);
        var viewModel = new SettingsPageViewModel([forcedMode], []);

        Assert.False(viewModel.CanUseForcedMode);
        Assert.False(forcedMode.IsEnabled);
        Assert.True(forcedMode.IsVipRestricted);

        viewModel.SetUserAccess(true, false);
        forcedMode.IsEnabled = true;

        Assert.False(viewModel.CanUseForcedMode);
        Assert.False(forcedMode.IsEnabled);
        Assert.True(forcedMode.IsVipRestricted);

        viewModel.SetUserAccess(true, true);
        forcedMode.IsEnabled = true;

        Assert.True(viewModel.CanUseForcedMode);
        Assert.True(forcedMode.IsEnabled);
        Assert.False(forcedMode.IsVipRestricted);

        viewModel.SetUserAccess(false, true);

        Assert.False(viewModel.CanUseForcedMode);
        Assert.False(forcedMode.IsEnabled);
        Assert.True(forcedMode.IsVipRestricted);
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
    public void ExportRecordsEntry_OpensTheUiOnlyExportModal()
    {
        var export = new SettingsEntryItemViewModel("ExportRecords", "Export", "Description", "Icon");
        var viewModel = new SettingsPageViewModel([], [export]);

        viewModel.ActivateEntryCommand.Execute(export);

        Assert.True(viewModel.ExportRecordsModal.IsOpen);
        Assert.Equal("ExportRecords", viewModel.LastActivatedEntryKey);
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
        Assert.Equal(21 * 60, modal.EndValue);
        Assert.Equal(string.Empty, modal.SelectedDurationText);

        modal.ConfirmCommand.Execute(null);
        Assert.True(modal.IsOpen);
        Assert.Equal("请选择开始时间和结束时间", modal.ValidationMessage);

        modal.AdjustTimeByWheel(false, -120);
        Assert.Equal("20:55", modal.EndTimeText);
        Assert.Equal(20 * 60 + 55, modal.EndValue);
    }

    [Fact]
    public void TimeRange_ClampsBothHandlesToTwelveHours()
    {
        var modal = CreateRuleModal();
        modal.Open();

        modal.EndValue = 23 * 60;

        Assert.Equal(21 * 60, modal.EndValue);
        Assert.Equal("21:00", modal.EndTimeText);
        Assert.Equal("12小时 · 已达上限", modal.SelectedDurationText);

        modal.EndValue = 18 * 60;
        modal.StartValue = 0;

        Assert.Equal(6 * 60, modal.StartValue);
        Assert.Equal("06:00", modal.StartTimeText);
        Assert.Equal("12小时 · 已达上限", modal.SelectedDurationText);
    }

    [Fact]
    public void TimeInputs_UseTheSameTwelveHourBoundaryAndUpdateDurationText()
    {
        var modal = CreateRuleModal();
        modal.Open();

        Assert.Equal("3小时", modal.SelectedDurationText);

        modal.EndTimeText = "23:00";

        Assert.Equal("21:00", modal.EndTimeText);
        Assert.Equal(21 * 60, modal.EndValue);
        Assert.Equal("12小时 · 已达上限", modal.SelectedDurationText);

        modal.EndTimeText = "10:35";

        Assert.Equal("1小时 35分钟", modal.SelectedDurationText);
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
    public void NewRule_IsDisabledUntilTheUserExplicitlyEnablesIt()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");

        modal.Open();
        modal.ConfirmCommand.Execute(null);

        Assert.False(Assert.Single(viewModel.AutomaticRules).IsEnabled);
    }

    [Fact]
    public void EditRule_LoadsExistingValuesAndUpdatesOriginalItemInPlace()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");

        modal.Open();
        modal.SelectCustomCommand.Execute(null);
        modal.EndTimeText = "18:00";
        modal.StartTimeText = "14:00";
        modal.ConfirmCommand.Execute(null);

        var originalRule = Assert.Single(viewModel.AutomaticRules);
        var originalId = originalRule.Id;
        originalRule.IsEnabled = false;

        viewModel.EditRuleCommand.Execute(originalRule);

        Assert.True(modal.IsOpen);
        Assert.True(modal.IsEditing);
        Assert.True(modal.IsCustom);
        Assert.Equal("周一、周三、周五", modal.SelectedDaysText);
        Assert.Equal("14:00", modal.StartTimeText);
        Assert.Equal("18:00", modal.EndTimeText);

        modal.Weekdays.Single(day => day.Key == "Tuesday").IsSelected = true;
        modal.StartTimeText = "15:00";
        modal.EndTimeText = "19:00";
        modal.ConfirmCommand.Execute(null);

        var updatedRule = Assert.Single(viewModel.AutomaticRules);
        Assert.Same(originalRule, updatedRule);
        Assert.Equal(originalId, updatedRule.Id);
        Assert.False(updatedRule.IsEnabled);
        Assert.True(updatedRule.IsCustom);
        Assert.Equal("周一 / 周二 / 周三 / 周五", updatedRule.RepeatText);
        Assert.Equal("15:00 – 19:00", updatedRule.TimeRangeText);
        Assert.Equal(900, updatedRule.StartMinutes);
        Assert.Equal(1140, updatedRule.EndMinutes);
        Assert.False(modal.IsOpen);

        viewModel.OpenRuleModalCommand.Execute(null);
        Assert.False(modal.IsEditing);
    }

    [Fact]
    public void EditRule_UsesSharedValidationAndDoesNotOverwriteOnConflict()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");

        modal.Open();
        modal.ConfirmCommand.Execute(null);
        modal.Open();
        modal.EndTimeText = "18:00";
        modal.StartTimeText = "14:00";
        modal.ConfirmCommand.Execute(null);

        var ruleToEdit = viewModel.AutomaticRules[1];
        viewModel.EditRuleCommand.Execute(ruleToEdit);
        modal.StartTimeText = "09:00";
        modal.EndTimeText = "12:00";
        modal.ConfirmCommand.Execute(null);

        Assert.True(modal.IsOpen);
        Assert.Equal("已存在相同的自动屏蔽规则", modal.ValidationMessage);
        Assert.Equal(2, viewModel.AutomaticRules.Count);
        Assert.Same(ruleToEdit, viewModel.AutomaticRules[1]);
        Assert.Equal("14:00 – 18:00", ruleToEdit.TimeRangeText);
    }

    [Fact]
    public void RuleCreation_MergesOverlappingContainingAndTouchingIntervals()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");

        modal.Open();
        modal.ConfirmCommand.Execute(null);
        Assert.Single(viewModel.AutomaticRules);

        modal.Open();
        modal.StartTimeText = "11:00";
        modal.EndTimeText = "14:25";
        modal.ConfirmCommand.Execute(null);
        Assert.Single(viewModel.AutomaticRules);
        Assert.Equal("09:00 – 14:25", viewModel.AutomaticRules[0].TimeRangeText);
        Assert.True(viewModel.IsRuleMergeToastVisible);
        Assert.Equal("09:00 – 14:25", viewModel.RuleMergeToastRange);

        viewModel.CloseRuleMergeToastCommand.Execute(null);
        modal.Open();
        modal.StartTimeText = "10:00";
        modal.EndTimeText = "12:00";
        modal.ConfirmCommand.Execute(null);
        Assert.Single(viewModel.AutomaticRules);
        Assert.Equal("09:00 – 14:25", viewModel.AutomaticRules[0].TimeRangeText);

        modal.Open();
        modal.EndTimeText = "16:00";
        modal.StartTimeText = "15:00";
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(2, viewModel.AutomaticRules.Count);
        Assert.False(viewModel.IsRuleMergeToastVisible);
    }

    [Fact]
    public void RuleCreation_MergesAllConnectedIntervalsInOnePass()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");

        modal.Open();
        modal.StartTimeText = "09:00";
        modal.EndTimeText = "10:00";
        modal.ConfirmCommand.Execute(null);
        modal.Open();
        modal.StartTimeText = "11:00";
        modal.EndTimeText = "12:00";
        modal.ConfirmCommand.Execute(null);
        Assert.Equal(2, viewModel.AutomaticRules.Count);

        modal.Open();
        modal.StartTimeText = "10:00";
        modal.EndTimeText = "11:00";
        modal.ConfirmCommand.Execute(null);

        var merged = Assert.Single(viewModel.AutomaticRules);
        Assert.Equal("09:00 – 12:00", merged.TimeRangeText);
    }

    [Fact]
    public void RuleCreation_OverDailyLimitKeepsModalOpenAndDoesNotMutateRules()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");
        var existing = CreateRule("08:00 – 19:15", 8 * 60, 19 * 60 + 15, isEnabled: false);
        viewModel.AutomaticRules.Add(existing);
        var changes = 0;
        viewModel.RulesChanged += (_, _) => changes++;

        modal.Open();
        modal.EndTimeText = "20:30";
        modal.StartTimeText = "19:00";
        modal.ConfirmCommand.Execute(null);

        Assert.True(modal.IsOpen);
        Assert.Single(viewModel.AutomaticRules);
        Assert.Same(existing, viewModel.AutomaticRules[0]);
        Assert.Equal(0, changes);
        Assert.True(viewModel.IsRuleLimitToastVisible);
        Assert.Equal("周一每日最多屏蔽 12 小时，当前还可添加 45 分钟", viewModel.RuleLimitToastMessage);
        Assert.Equal(string.Empty, modal.ValidationMessage);

        viewModel.CloseRuleLimitToastCommand.Execute(null);
        Assert.False(viewModel.IsRuleLimitToastVisible);
    }

    [Fact]
    public void RuleCreation_WhenDayIsAtLimitShowsNoRemainingCapacity()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");
        viewModel.AutomaticRules.Add(CreateRule("00:00 – 12:00", 0, 12 * 60, isEnabled: false));

        modal.Open();
        modal.EndTimeText = "13:00";
        modal.StartTimeText = "12:00";
        modal.ConfirmCommand.Execute(null);

        Assert.True(modal.IsOpen);
        Assert.Single(viewModel.AutomaticRules);
        Assert.Equal("周一已达到每日 12 小时上限，无法继续添加", viewModel.RuleLimitToastMessage);
    }

    [Fact]
    public void RuleEdit_UsesReplacementInsteadOfCountingPreviousVersion()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var modal = CreateRuleModal();
        var viewModel = new SettingsPageViewModel([automatic], [], modal, "每天");
        var existing = CreateRule("08:00 – 19:00", 8 * 60, 19 * 60, isEnabled: false);
        viewModel.AutomaticRules.Add(existing);

        viewModel.EditRuleCommand.Execute(existing);
        modal.EndTimeText = "20:00";
        modal.ConfirmCommand.Execute(null);

        Assert.False(modal.IsOpen);
        Assert.Same(existing, Assert.Single(viewModel.AutomaticRules));
        Assert.Equal("08:00 – 20:00", existing.TimeRangeText);
        Assert.False(viewModel.IsRuleLimitToastVisible);
    }

    [Fact]
    public void EnablingAnActiveRule_RequiresConfirmationBeforeItStartsBlocking()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var now = new DateTime(2026, 8, 17, 18, 0, 0);
        var viewModel = new SettingsPageViewModel([automatic], [], clock: () => now);
        var rule = CreateRule("09:00 – 19:55", 540, 1195, isEnabled: false);
        viewModel.AutomaticRules.Add(rule);

        viewModel.ToggleRuleCommand.Execute(rule);

        Assert.True(viewModel.RuleActivationModal.IsOpen);
        Assert.False(rule.IsEnabled);

        viewModel.RuleActivationModal.ConfirmCommand.Execute(null);

        Assert.False(viewModel.RuleActivationModal.IsOpen);
        Assert.True(rule.IsEnabled);
    }

    [Fact]
    public void CancellingActiveRuleConfirmation_LeavesRuleDisabled()
    {
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var now = new DateTime(2026, 8, 17, 18, 0, 0);
        var viewModel = new SettingsPageViewModel([automatic], [], clock: () => now);
        var rule = CreateRule("09:00 – 19:55", 540, 1195, isEnabled: false);
        viewModel.AutomaticRules.Add(rule);

        viewModel.ToggleRuleCommand.Execute(rule);
        viewModel.RuleActivationModal.CloseCommand.Execute(null);

        Assert.False(viewModel.RuleActivationModal.IsOpen);
        Assert.False(rule.IsEnabled);
    }

    [Fact]
    public void EnablingRule_DoesNotConfirmWhenAutomaticBlockingIsOffOrAnotherRuleIsActive()
    {
        var now = new DateTime(2026, 8, 17, 18, 0, 0);
        var disabledAutomatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", false);
        var disabledViewModel = new SettingsPageViewModel([disabledAutomatic], [], clock: () => now);
        var disabledRule = CreateRule("09:00 – 19:55", 540, 1195, isEnabled: false);
        disabledViewModel.AutomaticRules.Add(disabledRule);

        disabledViewModel.ToggleRuleCommand.Execute(disabledRule);

        Assert.False(disabledViewModel.RuleActivationModal.IsOpen);
        Assert.True(disabledRule.IsEnabled);

        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var activeViewModel = new SettingsPageViewModel([automatic], [], clock: () => now);
        var activeRule = CreateRule("09:00 – 19:55", 540, 1195);
        var pendingRule = CreateRule("17:00 – 20:00", 1020, 1200, isEnabled: false);
        activeViewModel.AutomaticRules.Add(activeRule);
        activeViewModel.AutomaticRules.Add(pendingRule);

        activeViewModel.ToggleRuleCommand.Execute(pendingRule);

        Assert.False(activeViewModel.RuleActivationModal.IsOpen);
        Assert.True(pendingRule.IsEnabled);
    }

    [Fact]
    public void EnablingCustomAndOvernightRules_UsesTheApplicableRuleDay()
    {
        var tuesdayAtOne = new DateTime(2026, 8, 18, 1, 0, 0);
        var automatic = new SettingsToggleItemViewModel("AutomaticBlocking", "Automatic", "Description", "Icon", true);
        var viewModel = new SettingsPageViewModel([automatic], [], clock: () => tuesdayAtOne);
        var mondayOvernight = CreateRule(
            "21:00 – 02:00", 1260, 120, isEnabled: false, isCustom: true, dayKeys: ["Monday"]);
        viewModel.AutomaticRules.Add(mondayOvernight);

        viewModel.ToggleRuleCommand.Execute(mondayOvernight);

        Assert.True(viewModel.RuleActivationModal.IsOpen);
        Assert.False(mondayOvernight.IsEnabled);

        var tuesdayAtThree = new DateTime(2026, 8, 18, 3, 0, 0);
        var outsideWindowViewModel = new SettingsPageViewModel(
            [automatic], [], clock: () => tuesdayAtThree);
        var outsideWindowRule = CreateRule(
            "21:00 – 02:00", 1260, 120, isEnabled: false, isCustom: true, dayKeys: ["Monday"]);
        outsideWindowViewModel.AutomaticRules.Add(outsideWindowRule);

        outsideWindowViewModel.ToggleRuleCommand.Execute(outsideWindowRule);

        Assert.False(outsideWindowViewModel.RuleActivationModal.IsOpen);
        Assert.True(outsideWindowRule.IsEnabled);
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
    public void AutomaticBlocking_UsesTheRemainingRuleDurationForTheFocusSession()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var rule = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "09:00 – 10:00",
            ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"], 540, 600);

        home.EvaluateAutomaticBlocking(
            [rule], true, new DateTime(2026, 8, 17, 9, 30, 0));

        Assert.True(home.FocusSession.IsPreparing);
        Assert.Equal(30 * 60, home.FocusSession.TotalFocusSeconds);
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

    private static AutomaticRuleItemViewModel CreateRule(
        string range,
        double startMinutes,
        double endMinutes,
        bool isEnabled = true,
        bool isCustom = false,
        IEnumerable<string>? dayKeys = null)
    {
        var rule = new AutomaticRuleItemViewModel(
            Guid.NewGuid(),
            isCustom ? "自定义" : "每天",
            range,
            dayKeys ?? ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"],
            startMinutes,
            endMinutes,
            isCustom);
        rule.IsEnabled = isEnabled;
        return rule;
    }
}
