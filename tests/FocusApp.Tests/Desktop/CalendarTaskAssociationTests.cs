using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarTaskAssociationTests
{
    private static DateTimeOffset Local(int day, int hour = 12, int minute = 0) => new(new DateTime(2026, 9, day, hour, minute, 0));
    private static LocalTaskDto TaskData(string id, DateTimeOffset? completedAt, string goal = "goal") =>
        new(id, goal, id, true, 0, Local(1), Local(18)) { CompletedAtUtc = completedAt };
    private static LocalDataSnapshotDto State(params LocalTaskDto[] tasks) => new(
        1, [], [new LocalTargetDto("goal", "学习UE5", false, 0, Local(1), Local(18))], tasks, [], [], [],
        new LocalAppSettingsDto(false, true, true, true, false, false, "goal", Local(18)), [], []);
    private static void SelectDate(StatisticsOverviewViewModel model, DateTime date)
    {
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < date.Year * 12 + date.Month)
            model.NextCalendarMonthCommand.Execute(null);
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > date.Year * 12 + date.Month)
            model.PreviousCalendarMonthCommand.Execute(null);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(day => day.Date.Date == date.Date));
    }

    [Fact]
    public void BatchCompletionsAreCountedByTaskIdAndOnlyExplicitSessionMembershipAppearsOnRows()
    {
        var at = Local(19, 2, 53);
        var tasks = Enumerable.Range(0, 37).Select(index => TaskData($"task-{index}", at)).ToArray();
        LocalFocusSessionDto Session(int hour, IReadOnlyList<LocalFocusSessionTaskSnapshotDto> completed) => new(
            Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false, 1800, 1800,
            Local(19, hour), Local(19, hour), Local(19, hour, 30), Local(19, hour, 30),
            FocusCompletionKindDto.Natural, null, null, false, null, null, completed);
        var model = new StatisticsOverviewViewModel(false);
        var state = State(tasks) with { FocusSessions = [Session(1, []), Session(13, [])] };
        model.ApplyState(state);
        SelectDate(model, at.Date);
        Assert.Equal(0, model.SelectedDayCompletedTasks);
        Assert.Empty(model.SelectedDayCompletedTaskItems);
        Assert.Equal(2, model.SelectedDayRecords.Count);
        Assert.All(model.SelectedDayRecords, record => {
            Assert.Equal(0, record.CalendarCompletedTaskCount);
            Assert.False(record.HasCalendarCompletedTasks);
        });

        // Even identical timestamps/names represent different tasks. Only these explicit
        // members belong to the newly saved session; the remaining 34 are excluded.
        var snapshots = tasks.Take(3).Select((task, index) =>
            new LocalFocusSessionTaskSnapshotDto(task.TaskId, "批量任务", index) { CompletedAtUtc = at }).ToArray();
        var valid = Session(2, snapshots) with { FocusStartedAtUtc = Local(19, 2, 30), CompletedAtUtc = Local(19, 3) };
        model.ApplyState(state with { FocusSessions = [.. state.FocusSessions, valid] });
        SelectDate(model, at.Date);
        Assert.Equal(3, model.SelectedDayCompletedTasks);
        var linked = model.SelectedDayRecords.Single(record => record.SessionId == valid.SessionId);
        Assert.Equal(3, linked.CalendarCompletedTaskCount);
        Assert.Equal("3项", linked.CalendarCompletedTaskCountDisplay);
        Assert.Equal(3, model.SelectedDayRecords.Sum(record => record.CalendarCompletedTaskCount));
        Assert.All(model.SelectedDayRecords.Where(record => record != linked), record => Assert.False(record.HasCalendarCompletedTasks));
    }

    [Fact]
    public void ValidHistoricalTasksRemainCountedAfterLiveTaskDeletionOrCompletionChanges()
    {
        var at = Local(19, 10, 1);
        var task = TaskData("saved", at);
        var session = new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
            300, 300, Local(19, 10), Local(19, 10), Local(19, 10, 5), Local(19, 10, 5),
            FocusCompletionKindDto.EarlyEnd, "goal", "学习UE5", false, null, null,
            [new("saved", "当时完成的任务", 0) { CompletedAtUtc = at }]);
        var model = new StatisticsOverviewViewModel(false);
        model.ApplyState(State(task) with { FocusSessions = [session] });
        SelectDate(model, at.Date);
        Assert.Equal("当时完成的任务", Assert.Single(model.SelectedDayCompletedTaskItems).Name);
        model.ApplyState(State() with { FocusSessions = [session] });
        SelectDate(model, at.Date);
        Assert.Equal(1, model.SelectedDayCompletedTasks);
        Assert.Equal("saved", Assert.Single(model.SelectedDayCompletedTaskItems).TaskId);
    }

    [Theory]
    [InlineData(299, 0)]
    [InlineData(300, 37)]
    public void CalendarCountsOnlySessionsAtOrAboveFiveActualMinutes(int seconds, int expected)
    {
        var at = Local(19, 10, 1);
        var snapshots = Enumerable.Range(0, 37).Select(index =>
            new LocalFocusSessionTaskSnapshotDto($"task-{index}", "同名批量任务", index) { CompletedAtUtc = at }).ToArray();
        var model = new StatisticsOverviewViewModel(false);
        model.ApplyState(State());
        var record = new FocusSessionRecordViewModel(Local(19, 10).DateTime, Local(19, 10, 30).DateTime, "goal", "学习UE5", "", 0)
            { CompletedTaskSnapshots = snapshots, RecordedFocusDuration = TimeSpan.FromSeconds(seconds) };
        model.FocusSessionRecords.Add(record);
        SelectDate(model, at.Date);
        Assert.Equal(expected, model.SelectedDayCompletedTasks);
        Assert.Equal(expected, record.CalendarCompletedTaskCount);
        Assert.Equal(expected, new CalendarRecordPoptipViewModel(record).AllRows.Count);
    }

    [Fact]
    public void ShortFocusCompletionsDoNotCreateAnExtraTaskSectionOrInflateTheCalendarTotal()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var date = Local(19).Date;
                var model = new StatisticsOverviewViewModel(false);
                model.SetUserAccess(true, true);
                model.SelectCalendarCommand.Execute(null);
                model.ApplyState(State(Enumerable.Range(0, 37).Select(index => TaskData($"任务{index:00}", Local(19, 2, 53))).ToArray()));
                model.FocusSessionRecords.Add(new(date.AddHours(1), date.AddHours(1.5), "", "", "", 0));
                model.FocusSessionRecords.Add(new(date.AddHours(13), date.AddHours(13.5), "", "", "", 0));
                SelectDate(model, date);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                var window = new Window { Width = 1000, Height = 760, Content = page, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
                try
                {
                    window.Show();
                    void Pump() { page.UpdateLayout(); page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); page.UpdateLayout(); }
                    Pump();
                    var scroll = (ScrollViewer)page.FindName("CalendarRecordsScrollViewer");
                    var timeline = (CalendarFocusTimeline)page.FindName("CalendarDayTimeline");
                    Assert.Equal(0, model.SelectedDayCompletedTasks);
                    Assert.Empty(model.SelectedDayCompletedTaskItems);
                    Assert.Null(page.FindName("CalendarUnrecordedCompletedTasks"));
                    Assert.IsType<ItemsControl>(scroll.Content);
                    Assert.Equal(2, timeline.Timeline!.Segments.Count);
                    Assert.Null(page.CalendarRecordInteraction.SelectedRecord);
                    Assert.All(model.SelectedDayRecords, record => Assert.False(record.HasCalendarCompletedTasks));
                    var count = (TextBlock)page.FindName("CalendarDayCompletedTaskCount");
                    Assert.Equal("0", count.Inlines.OfType<System.Windows.Documents.Run>().First().Text);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) throw new InvalidOperationException("Valid calendar task UI failed", failure);
    }

}
