using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class TodayFocusSummaryTests
{
    [Fact]
    public void TargetModesShareLeftAlignmentAndRefreshDisplayedValues()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                var page = CreatePage(model);
                Layout(page);
                var card = (Border)page.FindName("TodayStatisticsCard");
                var empty = (Grid)page.FindName("TodayFocusEmptyState");
                var daily = (Grid)page.FindName("TodayFocusTargetState");
                var monthly = (Grid)page.FindName("TodayFocusMonthlyTargetState");
                var originalHeight = card.ActualHeight;
                Assert.Equal(Visibility.Visible, empty.Visibility);
                Assert.Contains(Descendants<TextBlock>(empty), text => text.Text == "未设置今日目标，让专注更有方向");
                Assert.Contains(Descendants<TextBlock>(empty), text => text.Text == "今日 0 次专注");
                Assert.Empty(Descendants<ProgressBar>(empty));
                var setting = (Button)page.FindName("SetTodayFocusTargetButton");
                setting.Command.Execute(setting.CommandParameter);
                Assert.True(model.FocusGoalSettingsModal.IsOpen);
                model.FocusGoalSettingsModal.CancelCommand.Execute(null);
                SavePreview(card, "empty");

                model.FocusGoalSettingsModal.DailyTargetHoursInput = "4";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                Layout(page);
                Assert.Equal(originalHeight, card.ActualHeight);
                Assert.Equal(Visibility.Visible, daily.Visibility);
                Assert.Equal(Visibility.Collapsed, empty.Visibility);
                Assert.Equal(Visibility.Collapsed, monthly.Visibility);
                AssertAligned(daily, card);
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "本日 0 分钟 / 4 小时");
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "剩余 4 小时");
                Assert.DoesNotContain(Descendants<TextBlock>(daily), text => text.Text.Contains("次专注"));
                var dailyProgress = (ProgressBar)page.FindName("TodayFocusTargetStateProgress");
                var node = (FrameworkElement)dailyProgress.Template.FindName("ProgressNode", dailyProgress);
                Assert.Equal(Visibility.Collapsed, node.Visibility);
                SavePreview(card, "daily-zero");

                var todayEnd = DateTime.Today.AddHours(12);
                var todayRecord = new FocusSessionRecordViewModel(todayEnd.AddMinutes(-129), todayEnd, "goal", "学习", "", 0);
                model.FocusSessionRecords.Add(todayRecord);
                Layout(page);
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "本日 2 小时 9 分钟 / 4 小时");
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "剩余 1 小时 51 分钟");
                Assert.Equal(129 / 240d, dailyProgress.Value);
                Assert.Equal(Visibility.Visible, node.Visibility);
                AssertProgressNodeAtEnd(dailyProgress, node);
                var existingSetting = (Button)page.FindName("SetExistingTodayFocusTargetButton");
                Assert.Equal(Visibility.Visible, existingSetting.Visibility);
                Assert.True(Descendants<TextBlock>(existingSetting).Single().ActualWidth > 0);
                SavePreview(card, "daily");

                todayRecord.EndTime = todayRecord.StartTime.AddHours(4);
                Layout(page);
                Assert.Equal(1, dailyProgress.Value);
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "剩余 0 分钟");
                AssertProgressNodeAtEnd(dailyProgress, node);
                todayRecord.EndTime = todayRecord.StartTime.AddMinutes(309);
                Layout(page);
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "本日 5 小时 9 分钟 / 4 小时");
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "剩余 0 分钟");
                Assert.Equal(1, dailyProgress.Value);
                SavePreview(card, "daily-exceeded");

                existingSetting.Command.Execute(null);
                Assert.True(model.FocusGoalSettingsModal.IsOpen);
                model.FocusGoalSettingsModal.DeleteTargetCommand.Execute(null);
                Layout(page);
                Assert.Equal(originalHeight, card.ActualHeight);
                Assert.Equal(Visibility.Visible, empty.Visibility);
                Assert.Equal(Visibility.Collapsed, daily.Visibility);
                Assert.Contains(Descendants<TextBlock>(empty), text => text.Text == "今日 1 次专注");
                SavePreview(card, "removed-target");
                model.FocusSessionRecords.Remove(todayRecord);

                var end = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 12, 0, 0);
                var record = new FocusSessionRecordViewModel(end.AddMinutes(-264), end, "goal", "学习", "", 0);
                model.FocusSessionRecords.Add(record);
                model.FocusGoalSettingsModal.SelectMonthlyModeCommand.Execute(null);
                model.FocusGoalSettingsModal.MonthlyTargetHoursInput = "60";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                Layout(page);
                Assert.Equal(Visibility.Visible, monthly.Visibility);
                Assert.Equal(Visibility.Collapsed, daily.Visibility);
                AssertAligned(monthly, card);
                Assert.Equal(originalHeight, card.ActualHeight);
                Assert.Contains(Descendants<TextBlock>(monthly), text => text.Text == "本月 4 小时 24 分钟 / 60 小时");
                var days = Descendants<TextBlock>(monthly).Single(text => text.Text == $"剩余 {model.MonthlyFocusRemainingDays} 天");
                var progress = Descendants<ProgressBar>(monthly).Single();
                Assert.InRange(Math.Abs(days.TranslatePoint(new Point(days.ActualWidth, 0), card).X - progress.TranslatePoint(new Point(progress.ActualWidth, 0), card).X), 0, 1);
                SavePreview(card, "monthly");
                record.EndTime = end.AddMinutes(30);
                Layout(page);
                Assert.Contains(Descendants<TextBlock>(monthly), text => text.Text == "本月 4 小时 54 分钟 / 60 小时");
                Assert.Equal(model.MonthlyFocusTodayProgressRatio, progress.Value);
                Assert.DoesNotContain(Descendants<TextBlock>(card), text => text.Text.Contains("今日建议") || text.FontFamily.Source == "Segoe MDL2 Assets");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Today-focus UI verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Today-focus UI verification failed.", failure);
    }

    private static void AssertProgressNodeAtEnd(ProgressBar progress, FrameworkElement node)
    {
        var center = node.TranslatePoint(new Point(node.ActualWidth / 2, 0), progress).X;
        Assert.InRange(Math.Abs(center - progress.ActualWidth * progress.Value), 0, 1);
    }

    private static StatisticsPage CreatePage(StatisticsOverviewViewModel model)
    {
        var page = new StatisticsPage { DataContext = model };
        foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
            page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
        return page;
    }

    private static void AssertAligned(Grid state, Border card)
    {
        var labels = Descendants<TextBlock>(state).ToArray();
        var title = labels.Single(text => text.Text == "今日专注");
        var value = labels.Single(text => text.FontSize == 40);
        var progress = Descendants<ProgressBar>(state).Single();
        var x = title.TranslatePoint(new Point(0, 0), card).X;
        Assert.InRange(Math.Abs(value.TranslatePoint(new Point(0, 0), card).X - x), 0, 1);
        Assert.InRange(Math.Abs(progress.TranslatePoint(new Point(0, 0), card).X - x), 0, 1);
    }
    private static void Layout(StatisticsPage page)
    {
        page.Measure(new Size(728, 667));
        page.Arrange(new Rect(0, 0, 728, 667));
        page.UpdateLayout();
        page.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private static void SavePreview(Border card, string mode)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_TODAY_QA_PATH");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        // A fresh visual avoids reusing detached VisualBrush render resources between snapshots.
        var snapshotPage = CreatePage((StatisticsOverviewViewModel)card.DataContext);
        Layout(snapshotPage);
        card = (Border)snapshotPage.FindName("TodayStatisticsCard");
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(card), null, new Rect(0, 0, card.ActualWidth, card.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"today-focus-{mode}.png"));
        encoder.Save(stream);
    }
}
