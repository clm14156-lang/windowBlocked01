using FocusApp.Core;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public class RuleTimelineTests
{
    [Fact]
    public void RuleManagementEditSelectsRuleAndNormalOpenClearsSelection()
    {
        var settings = new SettingsPageViewModel([], [], dailyLabel: "每天");
        var first = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "01:00–02:00", ["Monday"], 60, 120);
        var second = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "03:00–04:00", ["Monday"], 180, 240);
        settings.EditRuleCommand.Execute(first);
        Assert.Equal(first.Id, settings.RuleModal.SelectedRuleId);
        settings.RuleModal.CancelEditor();
        Assert.Equal(first.Id, settings.RuleModal.SelectedRuleId);
        settings.EditRuleCommand.Execute(second);
        Assert.Equal(second.Id, settings.RuleModal.SelectedRuleId);
        settings.RuleModal.SelectedRuleId = null;
        Assert.Null(settings.RuleModal.SelectedRuleId);
        Assert.Equal(180, settings.RuleModal.StartValue);
        Assert.Equal(240, settings.RuleModal.EndValue);
        settings.RuleModal.OpenTimeline();
        Assert.Null(settings.RuleModal.SelectedRuleId);
        Assert.False(settings.RuleModal.IsEditorOpen);
    }

    [Fact]
    public void TargetOptionsUseDashAndExcludeArchivedTargets()
    {
        var now = DateTimeOffset.UtcNow;
        var modal = AutomaticRuleModalViewModel.CreateDefault();
        modal.ApplyTargets([
            new LocalTargetDto("goal-a", "当前目标 A", false, 0, now, now) { IconFileName = "code.png" },
            new LocalTargetDto("goal-archived", "归档目标", true, 1, now, now),
            new LocalTargetDto("goal-b", "当前目标 B", false, 2, now, now)
        ]);

        Assert.Equal(["-", "当前目标 A", "当前目标 B"], modal.Targets.Select(target => target.Name));
        Assert.DoesNotContain(modal.Targets, target => target.Id == "goal-archived");
        Assert.False(modal.Targets[0].HasIcon);
        Assert.True(modal.Targets[1].HasIcon);
        Assert.EndsWith("/code.png", modal.Targets[1].IconSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchivedHistoricalBindingIsTreatedAsUnboundAndSavedAsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var modal = AutomaticRuleModalViewModel.CreateDefault();
        modal.ApplyTargets([
            new LocalTargetDto("goal-active", "当前目标", false, 0, now, now),
            new LocalTargetDto("goal-archived", "归档目标", true, 1, now, now)
        ]);
        var rule = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "01:00–02:00",
            ["Monday"], 60, 120) { TargetId = "goal-archived" };
        AutomaticRuleDraft? submitted = null;
        modal.RuleSubmitted += (_, draft) => submitted = draft;

        modal.BeginEditor(rule);

        Assert.Null(modal.SelectedTargetId);
        Assert.Equal(string.Empty, modal.TargetChoice);
        Assert.True(modal.SaveEditor());
        Assert.NotNull(submitted);
        Assert.Null(submitted!.TargetId);
    }

    [Fact]
    public void TargetRefreshClearsSelectionWhenSelectedTargetBecomesArchived()
    {
        var now = DateTimeOffset.UtcNow;
        var modal = AutomaticRuleModalViewModel.CreateDefault();
        modal.ApplyTargets([new LocalTargetDto("goal-a", "当前目标", false, 0, now, now)]);
        modal.SelectedTargetId = "goal-a";

        modal.ApplyTargets([new LocalTargetDto("goal-a", "当前目标", true, 0, now, now)]);

        Assert.Null(modal.SelectedTargetId);
        Assert.Equal(["-"], modal.Targets.Select(target => target.Name));
    }

    [Fact]
    public void MovingPreviewSnapsClampsAndPreservesDurationWithoutConsideringNeighbors()
    {
        Assert.Equal((315d, 375d), RuleTimelineRange.Move(240, 300, 70));
        Assert.Equal((360d, 420d), RuleTimelineRange.Move(240, 300, 120));
        Assert.Equal((0d, 60d), RuleTimelineRange.Move(240, 300, -500));
        Assert.Equal((1380d, 1440d), RuleTimelineRange.Move(240, 300, 2000));
        Assert.Equal((345d, 406d), RuleTimelineRange.Move(242, 303, 100));
    }

    [Fact]
    public void ReleaseOverRuleSnapsAboveOrBelowItsCenterWithoutChangingDuration()
    {
        var above = RuleTimelineRange.ResolveMove(270, 330, 270, [(240, 360)]);
        var below = RuleTimelineRange.ResolveMove(270, 330, 300, [(240, 360)]);

        Assert.Equal((180d, 240d), above);
        Assert.Equal((360d, 420d), below);
        Assert.Equal(60, above!.Value.End - above.Value.Start);
        Assert.Equal(60, below!.Value.End - below.Value.Start);
    }

    [Fact]
    public void ReleaseFindsNearestLegalGapOrRestoresWhenNoGapFits()
    {
        Assert.Equal((60d, 120d), RuleTimelineRange.ResolveMove(
            270, 330, 250, [(120, 240), (240, 360), (420, 1440)]));
        Assert.Equal((600d, 660d), RuleTimelineRange.ResolveMove(
            600, 660, 630, [(120, 240)]));
        Assert.Null(RuleTimelineRange.ResolveMove(
            450, 1050, 700, [(0, 500), (940, 1440)]));
    }

    [Fact]
    public void ResizingEdgesSnapsAndStopsAtMinimumDurationAndNeighborBoundaries()
    {
        (double Start, double End)[] occupied = [(480, 548), (840, 900)];

        Assert.Equal((570d, 780d), RuleTimelineRange.Resize(600, 780, 571, true, occupied));
        Assert.Equal((548d, 780d), RuleTimelineRange.Resize(600, 780, 500, true, occupied));
        Assert.Equal((765d, 780d), RuleTimelineRange.Resize(600, 780, 779, true, occupied));
        Assert.Equal((600d, 825d), RuleTimelineRange.Resize(600, 780, 824, false, occupied));
        Assert.Equal((600d, 840d), RuleTimelineRange.Resize(600, 780, 900, false, occupied));
        Assert.Equal((600d, 615d), RuleTimelineRange.Resize(600, 780, 601, false, occupied));
    }

    [Fact]
    public void ResizingNeverLeavesTheDayOrExceedsTwelveHours()
    {
        Assert.Equal((60d, 780d), RuleTimelineRange.Resize(600, 780, -100, true, []));
        Assert.Equal((600d, 1320d), RuleTimelineRange.Resize(600, 780, 2000, false, []));
    }

    [Fact]
    public void MoveSavesOriginalRecordAndRejectsNewConflictsAndDurationChanges()
    {
        var settings = new SettingsPageViewModel([], []);
        var rule = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "04:00–05:00",
            ["Monday"], 240, 300) { TargetId = "goal-a", IsEnabled = false };
        settings.AutomaticRules.Add(rule);
        var updates = 0;
        settings.RulesChanged += (_, _) => updates++;
        Assert.Null(settings.RuleModal.MoveRequested!(rule, 255, 315));
        Assert.Same(rule, Assert.Single(settings.AutomaticRules));
        Assert.Equal(255, rule.StartMinutes);
        Assert.Equal("goal-a", rule.TargetId);
        Assert.False(rule.IsEnabled);
        Assert.False(settings.RuleModal.IsEditorOpen);
        Assert.Equal(1, updates);
        settings.AutomaticRules.Add(new(Guid.NewGuid(), "每天", "06:00–07:00", ["Monday"], 360, 420));
        Assert.NotNull(settings.RuleModal.MoveRequested!(rule, 330, 390));
        Assert.NotNull(settings.RuleModal.MoveRequested!(rule, 270, 315));
        Assert.Equal(255, rule.StartMinutes);
        Assert.Equal(1, updates);
        Assert.Null(settings.RuleModal.MoveRequested!(rule, 300, 360));
        Assert.Equal(2, updates);
    }

    [Fact]
    public void ResizeSavesChangedDurationAndStillRejectsConflicts()
    {
        var settings = new SettingsPageViewModel([], []);
        var rule = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "04:00–05:00",
            ["Monday"], 240, 300);
        settings.AutomaticRules.Add(rule);
        var updates = 0;
        settings.RulesChanged += (_, _) => updates++;

        Assert.Null(settings.RuleModal.ResizeRequested!(rule, 225, 300));
        Assert.Equal(225, rule.StartMinutes);
        Assert.Equal(300, rule.EndMinutes);
        Assert.Equal(1, updates);

        settings.AutomaticRules.Add(new(Guid.NewGuid(), "每天", "06:00–07:00", ["Monday"], 360, 420));
        Assert.NotNull(settings.RuleModal.ResizeRequested!(rule, 225, 390));
        Assert.NotNull(settings.RuleModal.ResizeRequested!(rule, 295, 300));
        Assert.Equal(300, rule.EndMinutes);
        Assert.Equal(1, updates);

        Assert.Null(settings.RuleModal.ResizeRequested!(rule, 225, 360));
        Assert.Equal(360, rule.EndMinutes);
        Assert.Equal(2, updates);
    }
    [Fact]
    public void AutomaticStartUsesBoundGoal()
    {
        var home = new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]);
        var now = DateTimeOffset.UtcNow;
        home.FocusTargetModal.ApplyState([
            new("goal-a", "目标 A", false, 0, now, now),
            new("goal-b", "目标 B", false, 1, now, now)], [], "goal-a");
        var rule = new AutomaticRuleItemViewModel(Guid.NewGuid(), "每天", "09:00–10:00",
            ["Monday"], 540, 600) { TargetId = "goal-b" };
        home.EvaluateAutomaticBlocking([rule], true, new DateTime(2026, 9, 7, 9, 30, 0));
        Assert.Equal("goal-b", home.FocusSession.ActiveTargetId);
        home.FocusSession.CancelPreparationCommand.Execute(null);
    }

    [Fact]
    public void EditorSurvivesSnapshotRefreshAndPreservesLegacyOvernightRange()
    {
        var settings = new SettingsPageViewModel([], []);
        var id = Guid.NewGuid();
        var dto = new FocusApp.Contracts.LocalAutomaticRuleDto(id, [DayOfWeek.Monday], 1380, 120, false, 0, true);
        settings.ApplyAutomaticRules([dto]);
        settings.EditRuleCommand.Execute(settings.AutomaticRules[0]);
        settings.ApplyAutomaticRules([dto]);
        var now = DateTimeOffset.UtcNow;
        settings.RuleModal.ApplyTargets([new LocalTargetDto("goal-b", "目标 B", false, 0, now, now)]);
        settings.RuleModal.SelectedTargetId = "goal-b";
        Assert.True(settings.RuleModal.SaveEditor());
        Assert.Equal(id, Assert.Single(settings.AutomaticRules).Id);
        Assert.Equal(120, settings.AutomaticRules[0].EndMinutes);
        Assert.Equal("goal-b", settings.AutomaticRules[0].TargetId);
    }
    [Fact]
    public void DragStopsAtOccupiedBoundaryAndRejectsOccupiedAnchor()
    {
        (double, double)[] occupied = [(180, 360)];
        Assert.Equal((60d, 180d), RuleTimelineRange.Drag(60, 420, occupied));
        Assert.Null(RuleTimelineRange.Drag(180, 420, occupied));
        Assert.Null(RuleTimelineRange.Drag(300, 420, occupied));
        Assert.Equal((360d, 390d), RuleTimelineRange.Drag(360, 388, occupied));
        Assert.Equal((60d, 90d), RuleTimelineRange.Drag(60, 92, occupied));
        Assert.Equal((60d, 60d), RuleTimelineRange.Drag(60, 30, occupied));
    }

    [Fact]
    public void UnsnappedBoundariesAndMidnightCannotBeCrossed()
    {
        Assert.Equal((60d, 182d), RuleTimelineRange.Drag(60, 200, [(182, 363)]));
        Assert.Equal((363d, 390d), RuleTimelineRange.Drag(364, 390, [(182, 363)]));
        Assert.Equal((1425d, 1440d), RuleTimelineRange.Drag(1430, 1600, []));
        Assert.Null(RuleTimelineRange.Drag(30, 90, [(1380, 120)]));
        Assert.False(RuleTimelineRange.Overlaps(60, 180, 180, 360));
        Assert.True(RuleTimelineRange.Overlaps(60, 181, 180, 360));
    }

    [Fact]
    public void EditorPreservesMinutesAndIdentityAndCancelDiscardsChanges()
    {
        var settings = new SettingsPageViewModel([], []);
        var modal = settings.RuleModal;
        var now = DateTimeOffset.UtcNow;
        modal.ApplyTargets([new LocalTargetDto("goal-a", "目标 A", false, 0, now, now)]);
        settings.OpenRuleModalCommand.Execute(null);
        Assert.True(modal.IsOpen);
        Assert.False(modal.IsEditorOpen);
        modal.BeginEditor(null, 60, 180);
        modal.EditorStartText = "01:02";
        modal.EditorEndText = "02:37";
        modal.SelectedTargetId = "goal-a";
        Assert.True(modal.SaveEditor());
        var rule = Assert.Single(settings.AutomaticRules);
        Assert.Equal(62, rule.StartMinutes);
        Assert.Equal(157, rule.EndMinutes);
        Assert.Equal("goal-a", rule.TargetId);
        settings.EditRuleCommand.Execute(rule);
        Assert.Equal("01:02", modal.EditorStartText);
        modal.EditorEndText = "03:00";
        modal.CancelEditor();
        Assert.Equal(157, rule.EndMinutes);
        settings.EditRuleCommand.Execute(rule);
        modal.EditorEndText = "03:00";
        Assert.True(modal.SaveEditor());
        Assert.Same(rule, Assert.Single(settings.AutomaticRules));
        Assert.Equal(180, rule.EndMinutes);
    }

    [Fact]
    public void SaveKeepsTimelineOpenAndOnlyClosesEditorAfterSuccess()
    {
        var settings = new SettingsPageViewModel([], []);
        var modal = settings.RuleModal;
        settings.OpenRuleModalCommand.Execute(null);
        modal.BeginEditor(null, 60, 120);
        modal.EditorEndText = "01:00";

        Assert.False(modal.SaveEditor());
        Assert.True(modal.IsOpen);
        Assert.True(modal.IsEditorOpen);
        Assert.NotEmpty(modal.ValidationMessage);

        modal.EditorEndText = "02:00";
        Assert.True(modal.SaveEditor());
        Assert.False(modal.IsEditorOpen);
        Assert.True(modal.IsOpen);
        Assert.Single(settings.AutomaticRules);
    }

    [Fact]
    public void FloatingEditorDeleteRemovesExistingRuleAndCancelsNewDraft()
    {
        var settings = new SettingsPageViewModel([], []);
        var existing = new AutomaticRuleItemViewModel(
            Guid.NewGuid(), "每天", "01:00–02:00", ["Monday"], 60, 120);
        settings.AutomaticRules.Add(existing);

        settings.RuleModal.BeginEditor(existing);
        settings.RuleModal.DeleteEditor();

        Assert.Empty(settings.AutomaticRules);
        Assert.False(settings.RuleModal.IsEditorOpen);

        settings.RuleModal.BeginEditor(null, 180, 240);
        settings.RuleModal.DeleteEditor();

        Assert.Empty(settings.AutomaticRules);
        Assert.False(settings.RuleModal.IsEditorOpen);
    }

    [Fact]
    public void SaveRejectsOverlapAllowsAdjacentAndNeverMerges()
    {
        var settings = new SettingsPageViewModel([], []);
        var modal = settings.RuleModal;
        settings.OpenRuleModalCommand.Execute(null);
        modal.BeginEditor(null, 180, 360);
        Assert.True(modal.SaveEditor());
        modal.BeginEditor(null, 60, 240);
        Assert.False(modal.SaveEditor());
        Assert.Contains("重叠", modal.ValidationMessage);
        Assert.Single(settings.AutomaticRules);
        modal.EditorEndText = "03:00";
        Assert.True(modal.SaveEditor());
        Assert.Equal(2, settings.AutomaticRules.Count);
        settings.EditRuleCommand.Execute(settings.AutomaticRules[0]);
        modal.EditorStartText = "02:00";
        Assert.False(modal.SaveEditor());
        modal.CancelEditor();
        Assert.Equal(2, settings.AutomaticRules.Count);
    }

    [Fact]
    public void CustomDaysAndInvalidTextAreValidatedBeforeSaving()
    {
        var settings = new SettingsPageViewModel([], []);
        var modal = settings.RuleModal;
        settings.OpenRuleModalCommand.Execute(null);
        modal.BeginEditor(null, 60, 120);
        modal.IsCustom = true;
        foreach (var day in modal.Weekdays) day.IsSelected = false;
        Assert.False(modal.SaveEditor());
        modal.Weekdays[0].IsSelected = true;
        modal.EditorEndText = "25:00";
        Assert.False(modal.SaveEditor());
        modal.EditorEndText = "01:00";
        Assert.False(modal.SaveEditor());
        modal.EditorEndText = "02:00";
        Assert.True(modal.SaveEditor());
        Assert.Single(Assert.Single(settings.AutomaticRules).DayKeys);
    }
}
