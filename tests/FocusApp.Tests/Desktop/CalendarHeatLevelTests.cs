using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CalendarHeatLevelTests
{
    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1, 1, "0.1h")]
    [InlineData(2, 1, "0.1h")]
    [InlineData(3, 1, "0.1h")]
    [InlineData(5, 1, "0.1h")]
    [InlineData(6, 1, "0.1h")]
    [InlineData(12, 1, "0.2h")]
    [InlineData(15, 1, "0.3h")]
    [InlineData(20, 1, "0.3h")]
    [InlineData(29, 1, "0.5h")]
    [InlineData(30, 2, "0.5h")]
    [InlineData(40, 2, "0.7h")]
    [InlineData(59, 2, "1.0h")]
    [InlineData(60, 3, "1.0h")]
    [InlineData(90, 3, "1.5h")]
    [InlineData(119, 3, "2.0h")]
    [InlineData(120, 4, "2.0h")]
    [InlineData(192, 4, "3.2h")]
    [InlineData(239, 4, "4.0h")]
    [InlineData(240, 5, "4.0h")]
    [InlineData(479, 5, "8.0h")]
    [InlineData(480, 6, "8.0h")]
    [InlineData(600, 6, "10.0h")]
    public void UsesExactMinuteBoundariesBeforeRoundingDisplay(int minutes, int level, string label)
    {
        var day = new CalendarDayViewModel(new DateTime(2026, 9, 7), true, minutes);
        Assert.Equal(level, day.HeatLevel);
        Assert.Equal(label, day.DurationLabel);
    }

    [Fact]
    public void AdjacentMonthNeverShowsHeatOrDuration()
    {
        var day = new CalendarDayViewModel(new DateTime(2026, 8, 31), false, 600);
        Assert.Equal(0, day.HeatLevel);
        Assert.Empty(day.DurationLabel);
        Assert.Equal(600, day.Minutes);
    }
}
