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
