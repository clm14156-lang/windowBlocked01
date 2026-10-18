using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
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
    [InlineData(30, 1, "0.5h")]
    [InlineData(40, 1, "0.7h")]
    [InlineData(59, 1, "1.0h")]
    [InlineData(60, 2, "1.0h")]
    [InlineData(90, 2, "1.5h")]
    [InlineData(119, 2, "2.0h")]
    [InlineData(120, 3, "2.0h")]
    [InlineData(192, 3, "3.2h")]
    [InlineData(239, 3, "4.0h")]
    [InlineData(240, 4, "4.0h")]
    [InlineData(479, 4, "8.0h")]
    [InlineData(480, 5, "8.0h")]
    [InlineData(600, 5, "10.0h")]
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

    [Fact]
    public void DailyTotalsColorOnlyDurationLabelsAndKeepDarkDatesAndTheSelectedStyle()
    {
        GoalDetailRedesignTests.RunSta(() =>
        {
            var today = new DateTime(2026, 10, 18);
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => today.AddDays(1));
            model.SetUserAccess(true, true); model.SelectCalendarCommand.Execute(null);
            var samples = new[] { (2, 30, "#EBB28C"), (4, 90, "#E69A6C"), (6, 180, "#E37F4C"),
                (8, 300, "#D9642D"), (10, 540, "#BF481F") };
            foreach (var (day, minutes, _) in samples)
            {
                var start = new DateTime(2026, 10, day, 8, 0, 0);
                model.FocusSessionRecords.Add(new(start, start.AddMinutes(minutes / 2), "g", "学习", "", 0));
                start = start.AddMinutes(minutes / 2 + 5);
                model.FocusSessionRecords.Add(new(start, start.AddMinutes(minutes / 2), "g", "学习", "", 0));
            }
            model.FocusSessionRecords.Add(new(today.AddHours(9), today.AddHours(9).AddMinutes(15), "g", "学习", "", 0));
            model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(day => day.Date == today));
            var page = GoalDetailRedesignTests.CreatePage(model);
            var window = new Window { Content = page, Width = 800, Height = 710, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); GoalDetailRedesignTests.Pump(page);
                var buttons = GoalDetailRedesignTests.Descendants<Button>(page)
                    .Where(button => button.DataContext is CalendarDayViewModel).ToArray();
                Button Day(int day) => buttons.Single(button => ((CalendarDayViewModel)button.DataContext).Date == new DateTime(2026, 10, day));
                TextBlock Duration(Button button) => (TextBlock)button.Template.FindName("DayDurationText", button);
                TextBlock Number(Button button) => (TextBlock)button.Template.FindName("DayNumberText", button);
                Border Background(Button button) => (Border)button.Template.FindName("DaySelectionBackground", button);
                foreach (var (day, minutes, text) in samples)
                {
                    var button = Day(day);
                    Assert.Equal(minutes, ((CalendarDayViewModel)button.DataContext).Minutes);
                    Assert.Equal((Color)ColorConverter.ConvertFromString(text), ((SolidColorBrush)Duration(button).Foreground).Color);
                    Assert.Equal(Color.FromRgb(55, 65, 81), ((SolidColorBrush)Number(button).Foreground).Color);
                    Assert.Equal(0, ((SolidColorBrush)Background(button).Background).Color.A);
                }
                model.SelectCalendarDateCommand.Execute(Day(10).DataContext); GoalDetailRedesignTests.Pump(page);
                Assert.Equal(Colors.White, ((SolidColorBrush)Duration(Day(10)).Foreground).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)Number(Day(10)).Foreground).Color);
                Assert.Equal(Color.FromRgb(255, 132, 31), ((SolidColorBrush)Background(Day(10)).Background).Color);
                model.SelectCalendarDateCommand.Execute(Day(18).DataContext); GoalDetailRedesignTests.Pump(page);
                Assert.Equal(Color.FromRgb(191, 72, 31), ((SolidColorBrush)Duration(Day(10)).Foreground).Color);
                Assert.Equal(Color.FromRgb(55, 65, 81), ((SolidColorBrush)Number(Day(10)).Foreground).Color);
                Assert.Equal(0, ((SolidColorBrush)Background(Day(10)).Background).Color.A);
                Day(10).Focus(); GoalDetailRedesignTests.Pump(page);
                Assert.Equal(0, ((SolidColorBrush)Background(Day(10)).Background).Color.A);
                Assert.Equal(Color.FromRgb(191, 72, 31), ((SolidColorBrush)Duration(Day(10)).Foreground).Color);
                Assert.Equal(Visibility.Collapsed, Duration(Day(17)).Visibility);
                Assert.Equal(Color.FromRgb(152, 162, 179), ((SolidColorBrush)Number(Day(17)).Foreground).Color);
                Assert.Equal(0, ((SolidColorBrush)Background(Day(17)).Background).Color.A);
            }
            finally { window.Close(); }
        });
    }
}
