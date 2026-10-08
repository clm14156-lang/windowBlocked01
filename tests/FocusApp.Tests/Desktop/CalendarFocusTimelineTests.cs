using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CalendarFocusTimelineTests
{
    [Fact]
    public void SessionsUseTheirRealPositionsDurationsAndGapsWithoutGrouping()
    {
        var day = new DateTime(2026, 10, 1);
        var first = Session(day.AddHours(9).AddMinutes(35), 10);
        var second = Session(day.AddHours(9).AddMinutes(55), 30);
        var third = Session(day.AddHours(14), 50);
        var timeline = CalendarFocusTimelineViewModel.Create(day, [third, second, first]);

        Assert.Equal(8, timeline.StartHour);
        Assert.Equal(3, timeline.Segments.Count);
        Assert.Same(first, timeline.Segments[0].Record);
        Assert.Equal(95d / 960, timeline.Segments[0].StartRatio, 10);
        Assert.Equal(115d / 960, timeline.Segments[1].StartRatio, 10);
        Assert.Equal(360d / 960, timeline.Segments[2].StartRatio, 10);
        Assert.Equal(3 * timeline.Segments[0].WidthRatio, timeline.Segments[1].WidthRatio, 10);
        Assert.Equal(10d / 960, timeline.Segments[1].StartRatio -
            timeline.Segments[0].StartRatio - timeline.Segments[0].WidthRatio, 10);
    }

    [Fact]
    public void MidnightSessionsAreClippedToTheSelectedDayAndEarlySessionsRemainVisible()
    {
        var day = new DateTime(2026, 10, 1);
        var incoming = Session(day.AddMinutes(-30), 60);
        var outgoing = Session(day.AddHours(23).AddMinutes(50), 30);
        var timeline = CalendarFocusTimelineViewModel.Create(day, [incoming, outgoing]);
        Assert.Equal(0, timeline.StartHour);
        Assert.Equal(day, timeline.Segments[0].StartTime);
        Assert.Equal(0, timeline.Segments[0].StartRatio);
        Assert.Equal(30d / 1440, timeline.Segments[0].WidthRatio, 10);
        Assert.Equal(day.AddDays(1), timeline.Segments[1].EndTime);
        Assert.Equal(10d / 1440, timeline.Segments[1].WidthRatio, 10);
        Assert.Equal(1, timeline.Segments[1].StartRatio + timeline.Segments[1].WidthRatio, 10);
        Assert.Equal("23:50 - 00:20", outgoing.TimeRangeDisplay);
        Assert.Equal("30 分钟", outgoing.CalendarDurationDisplay);

        var nextDay = CalendarFocusTimelineViewModel.Create(day.AddDays(1), [outgoing]);
        Assert.Equal(0, nextDay.StartHour);
        Assert.Equal(20d / 1440, Assert.Single(nextDay.Segments).WidthRatio, 10);
    }

    [Fact]
    public void SubMinuteSessionsStayProportionalAndInvalidOrOtherDaySessionsAreExcluded()
    {
        var day = new DateTime(2026, 10, 1);
        var seconds = Session(day.AddHours(12), 0.5);
        var timeline = CalendarFocusTimelineViewModel.Create(day,
            [seconds, Session(day.AddHours(13), 0), Session(day.AddHours(14), -10), Session(day.AddDays(1), 30)]);
        Assert.Equal(0.5 / 960, Assert.Single(timeline.Segments).WidthRatio, 10);
        var empty = CalendarFocusTimelineViewModel.Create(day, []);
        Assert.Equal(8, empty.StartHour);
        Assert.Empty(empty.Segments);
    }

    [Fact]
    public void TimelineTracksSelectionAndRecordEditsWhileListKeepsEachSession()
    {
        var model = new StatisticsOverviewViewModel(false);
        model.FocusSessionRecords.Clear();
        var day = DateTime.Today;
        var first = Session(day.AddHours(9), 10);
        var second = Session(day.AddHours(10), 30);
        model.FocusSessionRecords.Add(first);
        model.FocusSessionRecords.Add(second);
        model.FocusSessionRecords.Add(Session(day.AddDays(-1).AddHours(12), 40));
        model.ReturnToTodayCommand.Execute(null);
        Assert.Equal(2, model.SelectedDayRecords.Count);
        Assert.Equal(2, model.SelectedDayTimeline.Segments.Count);
        second.EndTime = second.StartTime.AddMinutes(50);
        Assert.Equal(0, model.SelectedDayTimeline.StartHour);
        Assert.Equal(50d / 1440, model.SelectedDayTimeline.Segments[1].WidthRatio, 10);
        Assert.Equal(60, model.SelectedDayMinutes);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date.Date == day.AddDays(-1)));
        Assert.Equal(40d / 1440, Assert.Single(model.SelectedDayTimeline.Segments).WidthRatio, 10);
        Assert.Single(model.SelectedDayRecords);
        model.FocusSessionRecords.Clear();
        Assert.Empty(model.SelectedDayTimeline.Segments);
        Assert.Empty(model.SelectedDayRecords);
    }

    [Fact]
    public void FullDayScaleStaysFixedForEmptyMorningEveningAndMidnightRecords()
    {
        var day = new DateTime(2026, 10, 1);
        Assert.Equal(0, CalendarFocusTimelineViewModel.Create(day, [], fullDay: true).StartHour);
        var records = new[] { Session(day.AddHours(1), 10), Session(day.AddHours(9).AddMinutes(35), 30),
            Session(day.AddHours(21), 50), Session(day.AddHours(23).AddMinutes(50), 30) };
        var timeline = CalendarFocusTimelineViewModel.Create(day, records, fullDay: true);
        Assert.Equal(0, timeline.StartHour);
        Assert.Equal(new[] { 60d / 1440, 575d / 1440, 1260d / 1440, 1430d / 1440 },
            timeline.Segments.Select(segment => segment.StartRatio));
        Assert.Equal(new[] { 10d / 1440, 30d / 1440, 50d / 1440, 10d / 1440 },
            timeline.Segments.Select(segment => segment.WidthRatio));
        Assert.Equal("23:50 - 00:20", timeline.Segments.Last().Record.TimeRangeDisplay);
        Assert.Equal("30 分钟", timeline.Segments.Last().Record.CalendarDurationDisplay);
        Assert.Equal(8, CalendarFocusTimelineViewModel.Create(day, [records[2]]).StartHour);
    }

    [Fact]
    public void CalendarContainsOnlyDailyMetricsTimelineAndIndependentRecordList()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln"))) directory = directory.Parent;
        var document = XDocument.Load(Path.Combine(directory!.FullName, "src", "FocusApp.Desktop", "Views", "StatisticsPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var calendar = document.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "CalendarPageLayout");
        Assert.DoesNotContain(calendar.Descendants(), element =>
            ((string?)element.Attribute("Text"))?.Contains("较上月") == true ||
            (string?)element.Attribute("Text") is "日均专注" or "时间分布");
        Assert.Single(calendar.Descendants().Where(element => element.Name.LocalName == "CalendarFocusTimeline" &&
            (string?)element.Attribute("Timeline") == "{Binding SelectedDayTimeline}"));
        Assert.Contains(calendar.Descendants(), element => (string?)element.Attribute("ItemsSource") == "{Binding SelectedDayRecords}");
        Assert.DoesNotContain(calendar.Descendants(), element => (string?)element.Attribute("ItemsSource") == "{Binding SelectedDayDistributions}");
    }

    private static FocusSessionRecordViewModel Session(DateTime start, double minutes) =>
        new(start, start.AddMinutes(minutes), "dev", "开发屏蔽软件", "", 0);
}
