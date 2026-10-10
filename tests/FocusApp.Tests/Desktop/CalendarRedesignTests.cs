using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarRedesignTests
{
    private static DateTime Date(int day, int hour = 9) => new(2026, 9, day, hour, 0, 0);
    private static void SelectDate(StatisticsOverviewViewModel model, DateTime date)
    {
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month < date.Year * 12 + date.Month)
            model.NextCalendarMonthCommand.Execute(null);
        while (model.CalendarMonth.Year * 12 + model.CalendarMonth.Month > date.Year * 12 + date.Month)
            model.PreviousCalendarMonthCommand.Execute(null);
        model.SelectCalendarDateCommand.Execute(model.CalendarDays.Single(day => day.Date.Date == date.Date));
    }

    private static void AddSession(StatisticsOverviewViewModel model, int day, int minutes, string goal = "a", int hour = 9) =>
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Date(day, hour), Date(day, hour).AddMinutes(minutes),
            goal, goal == "a" ? "window屏蔽软件" : "减肥到150斤", "", 0));

    [Fact]
    public void AvailabilityFollowsActualMinutesAndSelectionRecoversWhenRecordsAreRemoved()
    {
        var model = new StatisticsOverviewViewModel(false);
        SelectDate(model, Date(2));
        Assert.DoesNotContain(model.CalendarDays, day => day.IsSelected);
        AddSession(model, 2, 5);
        var selected = model.CalendarDays.Single(day => day.Date == Date(2).Date);
        Assert.True(selected.IsSelected);
        var empty = model.CalendarDays.Single(day => day.Date == Date(5).Date);
        Assert.False(empty.CanSelect);
        Assert.False(model.SelectCalendarDateCommand.CanExecute(empty));
        model.SelectCalendarDateCommand.Execute(empty);
        Assert.Equal(5, model.SelectedDayMinutes);
        AddSession(model, 5, 12);
        var added = model.CalendarDays.Single(day => day.Date == Date(5).Date);
        Assert.True(model.SelectCalendarDateCommand.CanExecute(added));
        model.SelectCalendarDateCommand.Execute(added);
        Assert.Equal(12, model.SelectedDayMinutes);
        model.FocusSessionRecords.Remove(model.FocusSessionRecords.Single(record => record.StartTime.Date == Date(5).Date));
        Assert.False(model.SelectCalendarDateCommand.CanExecute(added));
        model.SelectCalendarDateCommand.Execute(added);
        Assert.Equal(5, model.SelectedDayMinutes);
        Assert.True(model.CalendarDays.Single(day => day.Date == Date(2).Date).IsSelected);
        model.FocusSessionRecords.Clear();
        Assert.DoesNotContain(model.CalendarDays, day => day.IsSelected);
        Assert.Empty(model.SelectedDayRecords);
        Assert.Empty(model.SelectedDayDistributions);
        Assert.Empty(model.SelectedDayCompletedTaskItems);
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Date(2), Date(2).AddSeconds(20), "a", "学习", "", 0));
        Assert.False(model.CalendarDays.Single(day => day.Date == Date(2).Date).CanSelect);
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Date(1).Date.AddMinutes(-5), Date(1).Date.AddMinutes(5), "a", "学习", "", 0));
        var adjacent = model.CalendarDays.Single(day => !day.IsCurrentMonth && day.HasFocus);
        Assert.False(adjacent.CanSelect);
        Assert.True(model.CalendarDays.Single(day => day.Date == Date(1).Date).IsSelected);
        Assert.Equal(5, model.SelectedDayMinutes);
    }

    [Fact]
    public void MonthlyAverageUsesFocusDaysAndRefreshesWhenRecordsOrMonthChange()
    {
        var model = new StatisticsOverviewViewModel(false);
        SelectDate(model, Date(2));
        var changed = new List<string>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        AddSession(model, 2, 240);
        AddSession(model, 2, 60, hour: 14);
        AddSession(model, 3, 164);
        Assert.Equal(464, model.MonthlyTotalMinutes);
        Assert.Equal(2, model.MonthlyFocusDays);
        Assert.Equal(232, model.MonthlyAverageMinutes);
        Assert.Equal("3", model.MonthlyAverageHoursValueDisplay);
        Assert.Equal("52", model.MonthlyAverageMinutesValueDisplay);
        Assert.Contains(nameof(model.MonthlyAverageMinutes), changed);
        Assert.Contains(nameof(model.HasMonthlyFocusData), changed);
        model.NextCalendarMonthCommand.Execute(null);
        Assert.False(model.HasMonthlyFocusData);
        Assert.Equal("0", model.MonthlyTotalMinutesValueDisplay);
        Assert.Equal("—", model.MonthlyAverageMinutesValueDisplay);
        Assert.Equal("", model.MonthlyAverageMinutesUnitDisplay);
        model.PreviousCalendarMonthCommand.Execute(null);
        Assert.Equal(232, model.MonthlyAverageMinutes);
        model.FocusSessionRecords.Clear();
        Assert.False(model.HasMonthlyFocusData);
        Assert.Equal("—", model.MonthlyAverageMinutesValueDisplay);
    }

    [Fact]
    public void AShortSessionIsNotMistakenForAnEmptyMonth()
    {
        var model = new StatisticsOverviewViewModel(false);
        SelectDate(model, Date(2));
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(Date(2), Date(2).AddSeconds(20), "a", "学习", "", 0));
        Assert.Equal(0, model.MonthlyTotalMinutes);
        Assert.Equal(1, model.MonthlyFocusDays);
        Assert.True(model.HasMonthlyFocusData);
        Assert.Equal("0", model.MonthlyAverageMinutesValueDisplay);
        Assert.Equal("分钟", model.MonthlyAverageMinutesUnitDisplay);
    }

    [Fact]
    public void MonthlyComparisonsUsePreviousMonthDataAndHandleZeroBaseline()
    {
        var model = new StatisticsOverviewViewModel(false);
        SelectDate(model, Date(2));
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(new DateTime(2026, 8, 5, 9, 0, 0),
            new DateTime(2026, 8, 5, 10, 0, 0), "a", "学习", "", 0));
        AddSession(model, 2, 90);
        AddSession(model, 3, 90);
        Assert.Equal("较上月 +200%", model.MonthlyTotalComparisonDisplay);
        Assert.Equal("较上月 +1天", model.MonthlyFocusDaysComparisonDisplay);
        Assert.True(model.IsMonthlyFocusDaysIncrease);
        Assert.Equal("较上月 +50%", model.MonthlyAverageComparisonDisplay);
        model.NextCalendarMonthCommand.Execute(null);
        Assert.False(model.IsMonthlyFocusDaysIncrease);
        model.PreviousCalendarMonthCommand.Execute(null);
        model.FocusSessionRecords.Clear();
        Assert.Equal("较上月 0%", model.MonthlyTotalComparisonDisplay);
        Assert.Equal("较上月 0天", model.MonthlyFocusDaysComparisonDisplay);
        Assert.False(model.IsMonthlyFocusDaysIncrease);
    }

    [Fact]
    public void DateSelectionHeatAndMonthlyEmptyStateRenderWithoutChangingThePageSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                var now = new DateTimeOffset(Date(1));
                model.ApplyState(new LocalDataSnapshotDto(1, [],
                    [new LocalTargetDto("a", "window屏蔽软件", false, 0, now, now),
                     new LocalTargetDto("b", "减肥到150斤", false, 1, now, now)], [], [], [], [],
                    new LocalAppSettingsDto(false, true, true, true, false, false, "a", now), [], []));
                model.SetUserAccess(true, true);
                model.SelectCalendarCommand.Execute(null);
                AddSession(model, 2, 38);
                AddSession(model, 2, 7, "b", 12);
                AddSession(model, 3, 60);
                AddSession(model, 4, 42);
                AddSession(model, 6, 6);
                AddSession(model, 18, 60);
                AddSession(model, 19, 108);
                SelectDate(model, Date(2));
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 760, Height = 710, Content = page, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var layout = (Grid)page.FindName("CalendarPageLayout");
                Assert.Equal(page.ActualWidth - 32, layout.ActualWidth, 1);
                Assert.InRange(layout.ActualHeight, page.ActualHeight - 64, page.ActualHeight - 62);
                SavePreview(page, "calendar-populated");

                var dateButtons = Descendants<Button>(page).Where(button => button.DataContext is CalendarDayViewModel).ToArray();
                var ordinary = dateButtons.Single(button => ((CalendarDayViewModel)button.DataContext).Date == Date(3).Date);
                var selected = dateButtons.Single(button => ((CalendarDayViewModel)button.DataContext).Date == Date(2).Date);
                var background = (Border)ordinary.Template.FindName("DaySelectionBackground", ordinary);
                Assert.Equal(0, ((SolidColorBrush)background.Background).Color.A);
                var dateText = (TextBlock)ordinary.Template.FindName("DayNumberText", ordinary);
                var durationText = (TextBlock)ordinary.Template.FindName("DayDurationText", ordinary);
                Assert.NotEqual(((SolidColorBrush)dateText.Foreground).Color, ((SolidColorBrush)durationText.Foreground).Color);
                Assert.Equal(ColorConverter.ConvertFromString(((CalendarDayViewModel)ordinary.DataContext).HeatTextColor), ((SolidColorBrush)durationText.Foreground).Color);
                var selectedDateText = (TextBlock)selected.Template.FindName("DayNumberText", selected);
                var selectedDurationText = (TextBlock)selected.Template.FindName("DayDurationText", selected);
                var selectedBackground = (Border)selected.Template.FindName("DaySelectionBackground", selected);
                Assert.Equal(Colors.White, ((SolidColorBrush)selectedDateText.Foreground).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)selectedDurationText.Foreground).Color);
                Assert.Equal(Color.FromRgb(0xFF, 0x7A, 0x00), ((SolidColorBrush)selectedBackground.Background).Color);
                Assert.Equal(Visibility.Visible, selectedDurationText.Visibility);
                var selectedTop = selectedDateText.TranslatePoint(new Point(0, 0), selectedBackground).Y;
                var selectedBottom = selectedDurationText.TranslatePoint(new Point(0, selectedDurationText.ActualHeight), selectedBackground).Y;
                Assert.InRange(Math.Abs((selectedTop + selectedBottom) / 2 - selectedBackground.ActualHeight / 2), 0, 2);
                Assert.Equal(45, model.SelectedDayMinutes);
                Assert.Equal(2, model.SelectedDayDistributions.Count);
                Assert.Equal(1, model.SelectedDayDistributions.Sum(item => item.Ratio), 6);
                Assert.Equal(2, ((CalendarFocusTimeline)page.FindName("CalendarDayTimeline")).Timeline!.Segments.Count);

                var emptyDay = dateButtons.Single(button => ((CalendarDayViewModel)button.DataContext).Date == Date(5).Date);
                emptyDay.Command.Execute(emptyDay.CommandParameter);
                Pump();
                Assert.False(((CalendarDayViewModel)emptyDay.DataContext).IsSelected);
                Assert.False(emptyDay.IsEnabled);
                Assert.False(emptyDay.IsHitTestVisible);
                Assert.False(emptyDay.IsTabStop);
                Assert.Equal(System.Windows.Input.Cursors.Arrow, emptyDay.Cursor);
                Assert.False(emptyDay.Command.CanExecute(emptyDay.CommandParameter));
                Assert.Equal(45, model.SelectedDayMinutes);
                Assert.True(((Grid)page.FindName("CalendarPopulatedContent")).IsVisible);
                Assert.Null(page.FindName("DailyDistributionEmptyState"));
                Assert.False(((StackPanel)page.FindName("CalendarRecordsEmptyState")).IsVisible);
                Assert.True(((CalendarFocusTimeline)page.FindName("CalendarDayTimeline")).IsVisible);
                var emptyDateText = (TextBlock)emptyDay.Template.FindName("DayNumberText", emptyDay);
                var emptyDurationText = (TextBlock)emptyDay.Template.FindName("DayDurationText", emptyDay);
                var emptyBackground = (Border)emptyDay.Template.FindName("DaySelectionBackground", emptyDay);
                Assert.Equal(Visibility.Collapsed, emptyDurationText.Visibility);
                Assert.Equal(((SolidColorBrush)page.FindResource("DisabledText")).Color, ((SolidColorBrush)emptyDateText.Foreground).Color);
                Assert.Equal(0, ((SolidColorBrush)emptyBackground.Background).Color.A);
                var focusCard = (Border)page.FindName("CalendarDayMetrics");
                Assert.Equal(Colors.Transparent, ((SolidColorBrush)focusCard.Background).Color);
                Assert.Empty(Descendants<Image>(focusCard));
                var monthly = (Border)page.FindName("CalendarMonthlySummary");
                foreach (var label in new[] { "总专注时长", "专注天数" })
                {
                    var title = Descendants<TextBlock>(monthly).Single(text => text.Text == label);
                    var column = (StackPanel)((StackPanel)title.Parent).Parent;
                    var number = column.Children.OfType<Viewbox>().Single();
                    Assert.True(title.TranslatePoint(new Point(0, title.ActualHeight), monthly).Y <= number.TranslatePoint(new Point(), monthly).Y);
                }
                SavePreview(page, "calendar-disabled-date");

                model.NextCalendarMonthCommand.Execute(null);
                Pump();
                Assert.False(model.HasMonthlyFocusData);
                Assert.True(((Grid)page.FindName("CalendarPopulatedContent")).IsVisible);
                Assert.Empty(((CalendarFocusTimeline)page.FindName("CalendarDayTimeline")).Timeline!.Segments);
                Assert.True(((StackPanel)page.FindName("CalendarRecordsEmptyState")).IsVisible);
                Assert.True(((Border)page.FindName("CalendarMonthlySummary")).IsVisible);
                Assert.Null(page.FindName("MonthlyAverageDuration"));
                Assert.All(model.CalendarDays, day => Assert.False(day.HasFocus));
                Assert.DoesNotContain(model.CalendarDays, day => day.IsSelected);
                Assert.Equal(page.ActualWidth - 32, layout.ActualWidth, 1);
                foreach (var button in Descendants<Button>(page).Where(button => button.DataContext is CalendarDayViewModel))
                {
                    var number = (TextBlock)button.Template.FindName("DayNumberText", button);
                    Assert.InRange(Math.Abs(number.TranslatePoint(new Point(number.ActualWidth / 2, 0), button).X - button.ActualWidth / 2), 0, 1);
                }
                SavePreview(page, "calendar-empty-month");

                model.SetUserAccess(false, false);
                Pump();
                Assert.True(((Grid)page.FindName("DailyFocusRecordLockedPlaceholder")).IsVisible);
                model.PreviousCalendarMonthCommand.Execute(null);
                Pump();
                Assert.True(((Grid)page.FindName("DailyFocusRecordLockedPlaceholder")).IsVisible);
                Assert.False(((Grid)page.FindName("DailyFocusRecordContent")).IsVisible);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Calendar rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Calendar rendering verification failed.", failure);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void SavePreview(FrameworkElement element, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CALENDAR_REDESIGN_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        // Capture the settled selection highlight rather than a transitional animation frame.
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        Directory.CreateDirectory(directory);
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{name}.png"));
        encoder.Save(stream);
    }
}
