using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CalendarDayRecordViewModelTests
{
    private static readonly DateTime Day = new(2026, 10, 12);

    [Fact]
    public void DescendingRecordsUseActualNeighborGapsWithoutChangingTheSourceOrder()
    {
        var early = Session(8, 37, 45);
        var middle = Session(16, 43, 5);
        var latest = Session(18, 25, 15);
        var source = new[] { early, middle, latest };
        var entries = CalendarDayRecordViewModel.Create(source);
        Assert.Equal(new[] { latest, middle, early }, entries.Select(entry => entry.Record));
        Assert.Equal("间隔 1小时37分钟", entries[0].GapDisplay);
        Assert.Equal("间隔 7小时21分钟", entries[1].GapDisplay);
        Assert.False(entries[2].HasGap);
        Assert.Equal(new[] { early, middle, latest }, source);
        Assert.Equal("15分钟", entries[0].SummaryDisplay);
        Assert.False(entries[0].HasDetails);
    }

    [Fact]
    public void ParentCountIsDistinctAndAllChildSnapshotStatesAreRetained()
    {
        var record = withSnapshots();
        FocusSessionRecordViewModel withSnapshots() => new(Day.AddHours(9), Day.AddHours(9.5), "goal", "学习 UE5", "", 0)
        {
            CompletedTaskSnapshots =
            [
                new("p2", "整理房间", 1) { Details = new("", [new("整理书桌", true), new("清理地面", false)]) },
                new("p1", "学习 UE5", 0), new("p1", "重复快照", 3)
            ]
        };
        var entry = Assert.Single(CalendarDayRecordViewModel.Create([record]));
        Assert.Equal("30分钟 · 2个任务", entry.SummaryDisplay);
        Assert.Equal(new[] { "学习 UE5", "整理房间", "整理书桌", "清理地面" }, entry.Tasks.Select(task => task.Name));
        Assert.Equal(new[] { true, true, true, false }, entry.Tasks.Select(task => task.IsCompleted));
        Assert.True(entry.Tasks[3].IsLastChild);
        Assert.False(entry.IsExpanded);
        entry.IsExpanded = true;
        Assert.True(entry.IsExpanded);
        entry.IsExpanded = false;
        Assert.False(record.CompletedTaskSnapshots[0].Details!.SubTasks[1].IsCompleted);
        Assert.Equal(2, record.CalendarCompletedTaskCount);
    }

    [Fact]
    public void MissingGoalAndTasksCannotExpandAndRecordedFocusDurationRemainsAuthoritative()
    {
        var record = new FocusSessionRecordViewModel(Day.AddHours(10), Day.AddHours(11), "goal-unassigned", "自由专注", "", 0)
        { RecordedFocusDuration = TimeSpan.FromMinutes(15) };
        var entry = Assert.Single(CalendarDayRecordViewModel.Create([record]));
        Assert.False(entry.HasGoal);
        Assert.False(entry.HasTasks);
        entry.IsExpanded = true;
        Assert.False(entry.IsExpanded);
        Assert.Equal("15分钟", entry.SummaryDisplay);
        Assert.Empty(entry.Tasks);
    }

    [Fact]
    public void CrossMidnightGapsAndOverlapsDoNotInventNegativeIntervals()
    {
        var prior = new FocusSessionRecordViewModel(Day.AddDays(-1).AddHours(23.75), Day.AddMinutes(20), "", "", "", 0);
        var next = Session(0, 40, 10);
        var overlap = Session(0, 45, 10);
        var entries = CalendarDayRecordViewModel.Create([prior, overlap, next]);
        Assert.False(entries[0].HasGap);
        Assert.Equal(TimeSpan.FromMinutes(20), entries[1].Gap);
        Assert.False(entries[2].HasGap);
    }

    private static FocusSessionRecordViewModel Session(int hour, int minute, int duration, string goalId = "", string goalName = "")
    {
        var start = Day.AddHours(hour).AddMinutes(minute);
        return new(start, start.AddMinutes(duration), goalId, goalName, "", 0);
    }
}
