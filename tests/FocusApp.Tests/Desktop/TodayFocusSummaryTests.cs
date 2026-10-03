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
                Assert.Contains(Descendants<TextBlock>(empty), text => text.Text == "设定一个小目标，会让专注更有方向。");
                Assert.Empty(Descendants<ProgressBar>(empty));
                var setting = (Button)page.FindName("SetTodayFocusTargetButton");
                setting.Command.Execute(setting.CommandParameter);
                Assert.True(model.FocusGoalSettingsModal.IsOpen);
                model.FocusGoalSettingsModal.CancelCommand.Execute(null);

                model.FocusGoalSettingsModal.DailyTargetHoursInput = "4";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                Layout(page);
                Assert.Equal(originalHeight, card.ActualHeight);
                Assert.Equal(Visibility.Visible, daily.Visibility);
                Assert.Equal(Visibility.Collapsed, empty.Visibility);
                Assert.Equal(Visibility.Collapsed, monthly.Visibility);
                AssertAligned(daily, card);
                var progress = (ProgressBar)page.FindName("TodayFocusTargetStateProgress");
                var node = (FrameworkElement)progress.Template.FindName("ProgressNode", progress);
                Assert.Equal(Visibility.Visible, node.Visibility);
                AssertTip(progress, "0分钟 / 4小时", "4小时");
                Assert.DoesNotContain(Descendants<TextBlock>(daily), text => text.Text.Contains("今日目标"));

                var todayEnd = DateTime.Today.AddHours(12);
                var record = new FocusSessionRecordViewModel(todayEnd.AddMinutes(-129), todayEnd, "goal", "学习", "", 0);
                model.FocusSessionRecords.Add(record);
                Layout(page);
                Assert.Equal(129 / 240d, progress.Value);
                AssertTip(progress, "2小时9分钟 / 4小时", "1小时51分钟");
                AssertProgressNodeAtEnd(progress, node);

                record.EndTime = record.StartTime.AddMinutes(309);
                Layout(page);
                Assert.Equal(1, progress.Value);
                AssertTip(progress, "5小时9分钟 / 4小时", "0分钟");
                AssertProgressNodeAtEnd(progress, node);
                SavePreview(card, "daily-exceeded");

                model.FocusGoalSettingsModal.SelectMonthlyModeCommand.Execute(null);
                model.FocusGoalSettingsModal.MonthlyTargetHoursInput = "60";
                model.FocusGoalSettingsModal.SaveCommand.Execute(null);
                Layout(page);
                Assert.Equal(Visibility.Visible, monthly.Visibility);
                Assert.Equal(Visibility.Collapsed, daily.Visibility);
                Assert.Equal(originalHeight, card.ActualHeight);
                AssertAligned(monthly, card);
                var monthlyProgress = (ProgressBar)page.FindName("TodayFocusMonthlyTargetStateProgress");
                Assert.Equal(309 / 3600d, monthlyProgress.Value);
                AssertTip(monthlyProgress, "5小时9分钟", "5小时9分钟 / 60小时");
                var tip = (ToolTip)monthlyProgress.ToolTip;
                foreach (var ratio in new[] { 0d, 0.5d, 1d })
                {
                    monthlyProgress.SetCurrentValue(ProgressBar.ValueProperty, ratio);
                    var position = tip.CustomPopupPlacementCallback(new Size(340, 121), new Size(monthlyProgress.ActualWidth, 18), new Point())[0].Point;
                    var pointer = (FrameworkElement)tip.Template.FindName("FocusGoalTipPointer", tip);
                    Assert.InRange(Math.Abs(position.X + 8 + pointer.Margin.Left + 9 - ratio * monthlyProgress.ActualWidth), 0, 1);
                    Assert.InRange(position.X, -17, monthlyProgress.ActualWidth - 340 + 17);
                }
                monthlyProgress.GetBindingExpression(ProgressBar.ValueProperty)?.UpdateTarget();

                model.FocusGoalSettingsModal.DeleteTargetCommand.Execute(null);
                Layout(page);
                Assert.Equal(originalHeight, card.ActualHeight);
                Assert.Equal(Visibility.Visible, empty.Visibility);
                Assert.Equal(Visibility.Collapsed, daily.Visibility);
                Assert.Equal(Visibility.Collapsed, monthly.Visibility);
                Assert.DoesNotContain(Descendants<TextBlock>(card), text => text.IsVisible && text.Text.Contains("次专注"));
                SavePreview(card, "removed-target");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static void AssertTip(ProgressBar progress, string today, string detail)
    {
        var tip = (ToolTip)progress.ToolTip;
        tip.Measure(new Size(340, 121));
        tip.Arrange(new Rect(0, 0, 340, 121));
        tip.UpdateLayout();
        tip.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var content = (Grid)tip.Content;
        var values = Descendants<TextBlock>(content).Select(text => text.Text).ToArray();
        Assert.Contains(today, values);
        Assert.Contains(detail, values);
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
        var value = labels.First(text => text.FontSize == 50);
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
