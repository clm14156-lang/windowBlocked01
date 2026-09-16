using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                model.FocusGoalSettingsModal.DailyTargetHoursInput = "4";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                Layout(page);
                var card = (Border)page.FindName("TodayStatisticsCard");
                var daily = (Grid)page.FindName("TodayFocusTargetState");
                var monthly = (Grid)page.FindName("TodayFocusMonthlyTargetState");
                Assert.Equal(140, card.ActualHeight);
                Assert.Equal(Visibility.Visible, daily.Visibility);
                Assert.Equal(Visibility.Collapsed, monthly.Visibility);
                AssertAligned(daily, card);
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "/");
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "4");
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "小时");
                Assert.Contains(Descendants<TextBlock>(daily), text => text.Text == "还差 4 小时");
                Assert.Contains("今日 0 次专注", Descendants<TextBlock>(daily).Select(text => text.Text));
                SavePreview(card, "daily");

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
                var monthlyText = string.Concat(Descendants<TextBlock>(monthly).Select(text => text.Text));
                Assert.Contains($"/{model.MonthlyFocusTodayRecommendationDisplay}", monthlyText);
                Assert.Contains(Descendants<TextBlock>(monthly), text => text.Text == "本月 4 小时 24 分钟 / 60 小时");
                var days = Descendants<TextBlock>(monthly).Single(text => text.Text == $"剩余 {model.MonthlyFocusRemainingDays} 天");
                var progress = Descendants<ProgressBar>(monthly).Single();
                Assert.True(days.TranslatePoint(new Point(0, 0), card).X > progress.TranslatePoint(new Point(0, 0), card).X + progress.ActualWidth - 12);
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
