using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class FocusSessionEngineTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 26, 19, 0, 0);

    [Fact]
    public void Start_RejectsNonPositiveDuration()
    {
        var engine = CreateEngine();

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(0));
        Assert.Equal(FocusSessionState.Idle, engine.State);
    }

    [Fact]
    public void Preparation_TransitionsToFocusAndCarriesExcessElapsedTime()
    {
        var engine = CreateEngine();
        engine.Start(1);

        engine.AdvancePreparationBy(TimeSpan.FromSeconds(6));

        Assert.Equal(FocusSessionState.Focusing, engine.State);
        Assert.Equal(59, engine.RemainingFocusSeconds);
        Assert.Equal(1, engine.ElapsedFocusSeconds);
        Assert.Equal(1, engine.PreparationProgress);
    }

    [Fact]
    public void EarlyCompletion_CreatesOneRecordAndConfirmationFreezesTime()
    {
        var engine = CreateEngine();
        engine.Start(25);
        engine.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        engine.AdvanceFocusBy(TimeSpan.FromSeconds(10));

        Assert.True(engine.RequestEnd());
        Assert.Equal(1490, engine.RemainingFocusSeconds);
        engine.AdvanceFocusBy(TimeSpan.FromSeconds(10));
        Assert.Equal(1490, engine.RemainingFocusSeconds);

        Assert.True(engine.ConfirmEnd());
        Assert.Equal(FocusSessionState.Completed, engine.State);
        Assert.NotNull(engine.Completion);
        Assert.Equal(FocusCompletionKind.EarlyEnd, engine.Completion!.CompletionKind);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.Completion.ActualDuration);
        Assert.False(engine.ConfirmEnd());
    }

    [Fact]
    public void NaturalCompletion_RecordsFullDurationOnlyOnce()
    {
        var engine = CreateEngine();
        engine.Start(1);
        engine.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        engine.AdvanceFocusBy(TimeSpan.FromSeconds(60));

        Assert.Equal(FocusSessionState.Completed, engine.State);
        Assert.Equal(0, engine.RemainingFocusSeconds);
        Assert.Equal(TimeSpan.FromMinutes(1), engine.Completion!.ActualDuration);
        Assert.False(engine.AdvanceFocusBy(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CancelPreparation_ReturnsToIdleWithoutCompletion()
    {
        var engine = CreateEngine();
        engine.Start(25, forcedMode: true);

        Assert.True(engine.CancelPreparation());
        Assert.Equal(FocusSessionState.Idle, engine.State);
        Assert.False(engine.IsForcedMode);
        Assert.Null(engine.Completion);
    }

    [Fact]
    public void ForcedMode_CannotBeCompletedEarly()
    {
        var engine = CreateEngine();
        engine.Start(25, forcedMode: true);
        engine.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        engine.AdvanceFocusBy(TimeSpan.FromSeconds(10));

        Assert.False(engine.RequestEnd());
        Assert.False(engine.ConfirmEnd());
        Assert.Equal(FocusSessionState.Focusing, engine.State);
        Assert.Equal(1490, engine.RemainingFocusSeconds);
    }

    [Fact]
    public void Completion_PreservesTargetContextAndCompletedTaskIds()
    {
        var engine = CreateEngine();
        engine.Start(1, target: new FocusSessionTargetContext("target-1", "学习 Blender"));
        engine.UpdateCompletedTaskIds(["task-1", "task-2", "task-1"]);
        engine.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        engine.AdvanceFocusBy(TimeSpan.FromMinutes(1));

        Assert.NotNull(engine.Completion);
        var completion = engine.Completion!;
        Assert.Equal("target-1", completion.TargetId);
        Assert.Equal("学习 Blender", completion.TargetName);
        Assert.Equal(["task-1", "task-2"], completion.CompletedTaskIds);
    }

    [Fact]
    public void CancelPreparation_DoesNotRetainTargetContextOrTaskIds()
    {
        var engine = CreateEngine();
        engine.Start(25, target: new FocusSessionTargetContext("target-1", "学习 Blender"));
        engine.UpdateCompletedTaskIds(["task-1"]);

        Assert.True(engine.CancelPreparation());
        Assert.Null(engine.TargetId);
        Assert.Empty(engine.CompletedTaskIds);
        Assert.Null(engine.Completion);
    }

    private static FocusSessionEngine CreateEngine() => new(() => FixedNow);
}
