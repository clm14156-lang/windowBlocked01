using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CalendarStartupTests
{
    [Theory]
    [InlineData(2026, 2, 15)]
    [InlineData(2026, 10, 17)]
    [InlineData(2027, 1, 1)]
    public void CalendarStartsInTheCurrentLocalMonthWithoutSampleData(int year, int month, int day)
    {
        var now = new DateTime(year, month, day, 12, 0, 0);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now);

        Assert.Equal(new DateTime(year, month, 1), model.CalendarMonth);
        Assert.Equal(DateTime.DaysInMonth(year, month), model.CalendarDays.Count(item => item.IsCurrentMonth));
        Assert.Equal(0, model.MonthlyTotalMinutes);
    }

    [Fact]
    public void DeferredFirstOpenUsesTheMonthAtOpeningIncludingAcrossNewYear()
    {
        var now = new DateTime(2026, 12, 31, 23, 59, 0);
        var model = new StatisticsOverviewViewModel(false, deferInitialization: true, localNowProvider: () => now);
        now = new DateTime(2027, 1, 1, 0, 1, 0);

        model.EnsureInitialized();

        Assert.Equal(new DateTime(2027, 1, 1), model.CalendarMonth);
        Assert.Equal(42, model.CalendarDays.Count);
    }

    [Fact]
    public void PendingHistoryDoesNotSendFirstOpenBackToItsOldMonth()
    {
        var today = new DateTime(2026, 10, 17);
        var model = new StatisticsOverviewViewModel(false, deferInitialization: true, localNowProvider: () => today);
        model.ApplyState(State(new DateTime(2026, 2, 28), today));

        model.EnsureInitialized();

        Assert.Equal(new DateTime(2026, 10, 1), model.CalendarMonth);
        Assert.True(model.CalendarDays.Single(item => item.Date == today).IsSelected);
        Assert.Equal(30, model.SelectedDayMinutes);
        Assert.Equal(30, model.MonthlyTotalMinutes);
        Assert.Equal(2, model.FocusSessionRecords.Count);
    }

    [Fact]
    public void FirstPersistedSnapshotActuallyAppliesTheCurrentMonth()
    {
        var now = new DateTime(2026, 9, 30);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => now);
        now = new DateTime(2026, 10, 17);

        model.ApplyState(State(now));

        Assert.Equal(new DateTime(2026, 10, 1), model.CalendarMonth);
        Assert.True(model.CalendarDays.Single(item => item.Date == now).IsSelected);
        Assert.Equal(30, model.SelectedDayMinutes);
    }

    [Fact]
    public void LaterSnapshotsAndReopeningPreserveTheManuallyBrowsedMonthAndDate()
    {
        var today = new DateTime(2026, 10, 17);
        var previous = new DateTime(2026, 9, 12);
        var state = State(today, previous, previous.AddDays(1));
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => today);
        model.ApplyState(state);
        model.PreviousCalendarMonthCommand.Execute(null);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(item => item.Date == previous));

        model.ApplyState(state);
        model.EnsureInitialized();
        model.SelectOverviewCommand.Execute(null);
        model.SelectCalendarCommand.Execute(null);

        Assert.Equal(new DateTime(2026, 9, 1), model.CalendarMonth);
        Assert.True(model.CalendarDays.Single(item => item.Date == previous).IsSelected);
        Assert.Equal(60, model.MonthlyTotalMinutes);
    }

    [Fact]
    public void ReturnToTodayUsesTheSameLocalClockAsStartup()
    {
        var today = new DateTime(2027, 1, 1);
        var model = new StatisticsOverviewViewModel(false, localNowProvider: () => today);
        model.ApplyState(State(today, today.AddDays(-1)));
        model.PreviousCalendarMonthCommand.Execute(null);
        Assert.True(model.IsReturnToTodayVisible);

        model.ReturnToTodayCommand.Execute(null);

        Assert.Equal(new DateTime(2027, 1, 1), model.CalendarMonth);
        Assert.True(model.CalendarDays.Single(item => item.Date == today).IsSelected);
        Assert.False(model.IsReturnToTodayVisible);
    }

    private static LocalDataSnapshotDto State(params DateTime[] days)
    {
        var sessions = days.Select(day =>
        {
            var start = new DateTimeOffset(day.AddHours(9));
            return new LocalFocusSessionDto(Guid.NewGuid(), LocalFocusSessionStatusDto.Completed, false,
                1800, 1800, start, start, start.AddMinutes(30), start.AddMinutes(30),
                FocusCompletionKindDto.Natural, null, null, false, null, null, []);
        }).ToArray();
        return new LocalDataSnapshotDto(1, sessions, [], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, null, DateTimeOffset.Now), [], []);
    }
}
