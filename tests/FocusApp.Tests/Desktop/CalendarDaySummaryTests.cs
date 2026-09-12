using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CalendarDaySummaryTests
{
    [Fact]
    public void DistributionSplitsMidnightAndComparisonFollowsSelection()
    {
        var model = new StatisticsOverviewViewModel(false);
        var day = DateTime.Today;
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(day.AddMinutes(-30), day.AddMinutes(30), "a", "学习", "任务", 1));
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(day.AddHours(1), day.AddHours(2), "b", "开发", "", 0));
        model.ReturnToTodayCommand.Execute(null);
        Assert.Equal(90, model.SelectedDayMinutes);
        Assert.Equal(1, model.SelectedDayCompletedTasks);
        Assert.Equal("比昨日 +60 分钟", model.SelectedDayComparisonDisplay);
        Assert.Equal(new[] { 60, 30 }, model.SelectedDayDistributions.Select(item => item.Minutes));
        Assert.Equal(1d, model.SelectedDayDistributions.Sum(item => item.Ratio), 8);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date.Date == day.AddDays(-1)));
        Assert.Equal(30, Assert.Single(model.SelectedDayDistributions).Minutes);
    }
}
