using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalIconSelectionTests
{
    [Fact]
    public void NewGoalStartsWithTheFirstShortcutAndBlackForEveryIcon()
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        model.AddGoalCommand.Execute(null);

        Assert.Same(model.QuickTargetIcons[0], model.SelectedTargetIcon);
        Assert.Same(model.GoalColors[0], Assert.Single(model.GoalColors.Where(color => color.IsSelected)));
        Assert.Equal("#424547", model.SelectedGoalColorHex);
        AssertSynchronized(model);
    }

    [Fact]
    public void SelectingExistingShortcutsKeepsTheirPositionsAndTheLibraryOpen()
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        model.AddGoalCommand.Execute(null);
        model.SelectGoalColorCommand.Execute(model.GoalColors[6]);
        model.ToggleGoalIconLibraryCommand.Execute(null);
        var shortcuts = model.QuickTargetIcons.ToArray();
        var changes = 0;
        model.QuickTargetIcons.CollectionChanged += (_, _) => changes++;
        foreach (var icon in shortcuts.Reverse())
        {
            model.SelectTargetIconCommand.Execute(icon);
            Assert.Same(icon, model.SelectedTargetIcon);
            Assert.Equal(shortcuts, model.QuickTargetIcons);
            Assert.Equal("#A66AF3", model.SelectedGoalColorHex);
            Assert.True(model.IsGoalIconLibraryOpen);
            AssertSynchronized(model);
        }
        Assert.Equal(0, changes);
    }

    [Fact]
    public void SelectingAnAbsentIconOnlyReplacesTheFirstShortcut()
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        model.AddGoalCommand.Execute(null);
        var recentBefore = model.RecentTargetIconFileNames.ToArray();
        var otherShortcuts = model.QuickTargetIcons.Skip(1).ToArray();
        foreach (var name in new[] { "brain.svg", "heart.svg", "brain.svg" })
        {
            var icon = model.AllTargetIcons.Single(item => item.Id == name);
            model.SelectTargetIconCommand.Execute(icon);
            Assert.Same(icon, model.QuickTargetIcons[0]);
            Assert.Equal(otherShortcuts, model.QuickTargetIcons.Skip(1));
            AssertSynchronized(model);
        }
        var beforeSelectingExisting = model.QuickTargetIcons.ToArray();
        model.SelectTargetIconCommand.Execute(model.QuickTargetIcons[3]);
        Assert.Equal(beforeSelectingExisting, model.QuickTargetIcons);
        Assert.Equal(recentBefore, model.RecentTargetIconFileNames);
        AssertSynchronized(model);
    }

    [Fact]
    public void IconMatchingUsesItsStableIdInsteadOfObjectPositionOrColor()
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        model.AddGoalCommand.Execute(null);
        var canonical = model.QuickTargetIcons[2];
        var shortcuts = model.QuickTargetIcons.ToArray();
        var sameId = new TargetIconOptionViewModel(canonical.Id.ToUpperInvariant(), "unrelated-source#FFFFFF");

        model.SelectTargetIconCommand.Execute(sameId);

        Assert.Same(canonical, model.SelectedTargetIcon);
        Assert.Equal(canonical.Id, model.SelectedTargetIconId);
        Assert.Equal(shortcuts, model.QuickTargetIcons);
        AssertSynchronized(model);
    }

    [Fact]
    public void ChangingColorsUpdatesEveryIconWithoutChangingSelectionOrPositions()
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        model.AddGoalCommand.Execute(null);
        var icon = model.QuickTargetIcons[4];
        model.SelectTargetIconCommand.Execute(icon);
        model.ToggleGoalIconLibraryCommand.Execute(null);
        var shortcuts = model.QuickTargetIcons.ToArray();
        foreach (var color in model.GoalColors)
        {
            model.SelectGoalColorCommand.Execute(color);
            Assert.Same(icon, model.SelectedTargetIcon);
            Assert.Equal(shortcuts, model.QuickTargetIcons);
            Assert.True(model.IsGoalIconLibraryOpen);
            AssertSynchronized(model);
        }
    }

    [Fact]
    public void CancelDoesNotPersistDraftChoicesAndEditRestoresTheSavedIconAndColor()
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        model.AddGoalCommand.Execute(null);
        var initial = model.SelectedTargetIcon;
        var brain = model.AllTargetIcons.Single(item => item.Id == "brain.svg");
        model.SelectTargetIconCommand.Execute(brain);
        model.SelectGoalColorCommand.Execute(model.GoalColors[6]);
        model.CancelCreateGoalCommand.Execute(null);
        model.AddGoalCommand.Execute(null);
        Assert.Same(initial, model.SelectedTargetIcon);
        Assert.Equal("#424547", model.SelectedGoalColorHex);
        model.SelectTargetIconCommand.Execute(brain);
        model.SelectGoalColorCommand.Execute(model.GoalColors[5]);
        model.NewGoalName = "写作";
        model.ConfirmCreateGoalCommand.Execute(null);
        var goal = Assert.Single(model.Goals);
        Assert.Equal("brain.svg", goal.IconFileName);
        Assert.Equal("#299BFA", goal.IconColorHex);
        model.EditGoalCommand.Execute(goal);
        Assert.Same(brain, model.SelectedTargetIcon);
        Assert.Equal("#299BFA", model.SelectedGoalColorHex);
        AssertSynchronized(model);
    }

    private static void AssertSynchronized(StatisticsOverviewViewModel model)
    {
        Assert.Same(model.SelectedTargetIcon, Assert.Single(model.AllTargetIcons.Where(icon => icon.IsSelected)));
        Assert.Same(model.SelectedTargetIcon, Assert.Single(model.QuickTargetIcons.Where(icon => icon.IsSelected)));
        Assert.Equal(model.SelectedTargetIcon!.Id, model.SelectedTargetIconId);
        Assert.Single(model.GoalColors.Where(color => color.IsSelected));
        Assert.Equal(6, model.QuickTargetIcons.Count);
        Assert.Equal(6, model.QuickTargetIcons.Select(icon => icon.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(model.AllTargetIcons.Concat(model.QuickTargetIcons), icon => Assert.EndsWith(model.SelectedGoalColorHex, icon.DisplayIconSource));
    }
}
