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
public sealed class StatisticsPeriodDistributionTests
{
    private static StatisticsOverviewViewModel RuntimeModel()
    {
        var model = new StatisticsOverviewViewModel(false);
        var now = DateTimeOffset.Now;
        model.ApplyState(new LocalDataSnapshotDto(1, [],
            [new LocalTargetDto("a", "window屏蔽软件", false, 0, now, now),
             new LocalTargetDto("b", "减肥到150斤", false, 1, now, now)], [], [], [], [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", "a", now), [], []));
        return model;
    }

    private static FocusSessionRecordViewModel Add(StatisticsOverviewViewModel model, int daysAgo, int minutes, string goal = "a", int hour = 9)
    {
        var start = DateTime.Today.AddDays(-daysAgo).AddHours(hour);
        var record = new FocusSessionRecordViewModel(start, start.AddMinutes(minutes), goal,
            goal == "a" ? "window屏蔽软件" : goal == "b" ? "减肥到150斤" : goal, "", 0);
        model.FocusSessionRecords.Add(record);
        return record;
    }

    [Fact]
    public void RangeSwitchUsesTheSameDatesAsTrendAndRefreshesAfterRecordEdits()
    {
        var model = RuntimeModel();
        var recent = Add(model, 6, 30);
        Add(model, 7, 60, "b");
        Add(model, 29, 90, "a", 12);
        Add(model, 30, 120, "outside");
        Assert.Equal(30, model.PeriodFocusDistributionTotalMinutes);
        Assert.Equal(30, model.TrendPoints.Sum(day => day.Minutes));
        Assert.Equal("window屏蔽软件", Assert.Single(model.PeriodFocusDistributions).TargetName);
        model.SelectedRange = model.RangeOptions.Single(range => range.Days == 30);
        Assert.Equal("近30天趋势", model.TrendRangeTitle);
        Assert.Equal(180, model.PeriodFocusDistributionTotalMinutes);
        Assert.Equal(180, model.TrendPoints.Sum(day => day.Minutes));
        Assert.Equal(new[] { 120, 60 }, model.PeriodFocusDistributions.Select(item => item.Minutes));
        Assert.Equal(2d / 3, model.PeriodFocusDistributions[0].Ratio, 6);
        recent.EndTime = recent.StartTime.AddMinutes(45);
        Assert.Equal(195, model.PeriodFocusDistributionTotalMinutes);
        model.FocusSessionRecords.Remove(recent);
        model.SelectedRange = model.RangeOptions.Single(range => range.Days == 7);
        Assert.False(model.HasPeriodFocusDistribution);
        Assert.Equal("近七天没有专注记录", model.PeriodFocusDistributionEmptyTitle);
        model.SelectedRange = model.RangeOptions.Single(range => range.Days == 30);
        Assert.True(model.HasPeriodFocusDistribution);
        model.FocusSessionRecords.Clear();
        Assert.False(model.HasPeriodFocusDistribution);
        Assert.Equal("近30天没有专注记录", model.PeriodFocusDistributionEmptyTitle);
    }

    [Fact]
    public void DistributionClipsCrossMidnightAtTheRangeBoundaryAndKeepsSubMinuteSessions()
    {
        var model = RuntimeModel();
        var boundary = DateTime.Today.AddDays(-6);
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(boundary.AddMinutes(-30), boundary.AddMinutes(15), "a", "学习", "", 0));
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(DateTime.Today.AddDays(1).AddMinutes(-10),
            DateTime.Today.AddDays(1).AddMinutes(20), "b", "运动", "", 0));
        Assert.Equal(new[] { 15, 10 }, model.PeriodFocusDistributions.Select(item => item.Minutes));
        Assert.Equal(25, model.PeriodFocusDistributionTotalMinutes);
        Assert.Equal(25, model.TrendPoints.Sum(day => day.Minutes));
        model.FocusSessionRecords.Clear();
        model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(DateTime.Today.AddHours(9), DateTime.Today.AddHours(9).AddSeconds(20), "a", "学习", "", 0));
        Assert.True(model.HasPeriodFocusDistribution);
        Assert.Equal("<1 分钟", Assert.Single(model.PeriodFocusDistributions).DurationDisplay);
        Assert.Equal(1, model.PeriodFocusDistributions.Single().Ratio);
    }

    [Fact]
    public void OverviewRendersRangeSwitchingBoundedRowsAndIndependentTopGoalStates()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = RuntimeModel();
                model.SelectOverviewCommand.Execute(null);
                Add(model, 0, 38);
                Add(model, 0, 7, "b", 12);
                Add(model, 1, 7, "阅读");
                Add(model, 3, 7, "练习写作");
                Add(model, 12, 90, "历史目标");
                model.FocusGoalSettingsModal.SelectMonthlyModeCommand.Execute(null);
                model.FocusGoalSettingsModal.MonthlyTargetHoursInput = "60";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 760, Height = 710, Content = page, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var title = (TextBlock)page.FindName("PeriodFocusDistributionTitle");
                var list = (ItemsControl)page.FindName("PeriodFocusDistributionList");
                var scroll = (ScrollViewer)page.FindName("PeriodFocusDistributionScrollViewer");
                var top = (Border)page.FindName("TodayStatisticsCard");
                var progress = (ProgressBar)page.FindName("TodayFocusMonthlyTargetStateProgress");
                Assert.Equal("近7天趋势", title.Text);
                Assert.Equal(4, list.Items.Count);
                Assert.Equal(0, scroll.ScrollableHeight);
                Assert.True(((Grid)page.FindName("TodayFocusMonthlyTargetState")).IsVisible);
                Assert.True(progress.ActualWidth > top.ActualWidth - 50);
                Assert.DoesNotContain(Descendants<TextBlock>(top), text => text.IsVisible && text.Text.Contains('%'));
                SavePreview(page, "overview-populated");

                var range = Descendants<ComboBox>(page).Single(combo => ReferenceEquals(combo.ItemsSource, model.RangeOptions));
                range.SelectedItem = model.RangeOptions.Single(option => option.Days == 30);
                Pump();
                Assert.Equal("近30天趋势", title.Text);
                Assert.Equal(5, list.Items.Count);
                Assert.True(scroll.ScrollableHeight > 0);
                SavePreview(page, "overview-30-days");
                model.FocusSessionRecords.Clear();
                Pump();
                Assert.True(((StackPanel)page.FindName("PeriodFocusDistributionEmptyState")).IsVisible);
                Assert.False(scroll.IsVisible);
                Assert.True(((Grid)page.FindName("TodayFocusMonthlyTargetState")).IsVisible);
                Assert.Equal(30, model.TrendPoints.Count);
                SavePreview(page, "overview-empty-30-days");
                range.SelectedItem = model.RangeOptions[0];
                Pump();
                SavePreview(page, "overview-empty-7-days");

                model.FocusGoalSettingsModal.SelectDailyModeCommand.Execute(null);
                model.FocusGoalSettingsModal.DailyTargetHoursInput = "4";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                Pump();
                Assert.True(((Grid)page.FindName("TodayFocusTargetState")).IsVisible);
                Assert.False(((Grid)page.FindName("TodayFocusMonthlyTargetState")).IsVisible);
                Assert.True(((ProgressBar)page.FindName("TodayFocusTargetStateProgress")).ActualWidth > top.ActualWidth - 50);
                model.FocusGoalSettingsModal.DeleteTargetCommand.Execute(null);
                Pump();
                Assert.True(((Grid)page.FindName("TodayFocusEmptyState")).IsVisible);
                Assert.True(((Button)page.FindName("SetTodayFocusTargetButton")).IsVisible);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Overview rendering timed out.");
        if (failure is not null) throw new InvalidOperationException("Overview rendering verification failed.", failure);
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
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_OVERVIEW_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
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
