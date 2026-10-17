using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalPageRefinementTests
{
    [Fact]
    public void MetricCardsOpenExistingModalsAndExpandedDatesShowTheirSessionTasks()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var today = new DateTime(2026, 10, 13);
                var day = today.AddDays(-1);
                var model = new StatisticsOverviewViewModel(false, localNowProvider: () => today.AddHours(20));
                model.SetUserAccess(true, true);
                var goal = new GoalOverviewItemViewModel("goal", "我真的爱你哦啊", "", "", false, false, remark: string.Concat(Enumerable.Repeat("测试", 35)));
                model.Goals.Add(goal);
                foreach (var name in new[] { "Windows屏蔽软件开发", "休息玩手机", "12345678921345667" })
                    model.Goals.Add(new(Guid.NewGuid().ToString(), name, "", "", false, false));
                model.SelectGoalsCommand.Execute(null);
                model.SelectGoalCommand.Execute(goal);
                var snapshots = new LocalFocusSessionTaskSnapshotDto[]
                {
                    new("learn", "学习 UE5", 0),
                    new("room", "整理房间", 1) { Details = new("", [new("整理书桌", true), new("清理地面", true)]) },
                    new("write", "写文档", 2)
                };
                var latest = new FocusSessionRecordViewModel(day.AddHours(18).AddMinutes(25), day.AddHours(18).AddMinutes(40), goal.GoalId, goal.Name, "", 3, snapshots.Select(task => task.TaskNameSnapshot))
                { CompletedTaskIds = snapshots.Select(task => task.TaskId).ToArray(), CompletedTaskSnapshots = snapshots };
                var plain = new FocusSessionRecordViewModel(day.AddHours(16).AddMinutes(43), day.AddHours(16).AddMinutes(48), goal.GoalId, goal.Name, "", 0);
                var early = new FocusSessionRecordViewModel(day.AddHours(8).AddMinutes(37), day.AddHours(9).AddMinutes(22), goal.GoalId, goal.Name, "任务", 1);
                foreach (var record in new[] { early, plain, latest,
                             new FocusSessionRecordViewModel(today.AddHours(9), today.AddHours(9).AddMinutes(4), goal.GoalId, goal.Name, "", 0),
                             new FocusSessionRecordViewModel(day.AddDays(-2).AddHours(8), day.AddDays(-2).AddHours(10.5), goal.GoalId, goal.Name, "", 0) })
                    model.FocusSessionRecords.Add(record);
                var stamp = new DateTimeOffset(day.AddHours(19));
                var completed = Enumerable.Range(0, 13).Select(index => new LocalTaskDto($"live{index}", goal.GoalId, $"完成任务 {index}", true, index, stamp, stamp) { CompletedAtUtc = stamp }).ToArray();
                model.GoalCompletedTasks.ApplyState(goal, completed);
                var page = new StatisticsPage { DataContext = model, Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)) };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 726, Height = 710, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000, Content = page };
                window.Show(); Pump(page);
                var metrics = (Grid)page.FindName("GoalInvestmentMetrics");
                var total = (Button)page.FindName("GoalTotalInvestmentCard");
                var count = (Button)page.FindName("GoalCompletedTasksCard");
                Assert.Equal(2, Descendants<Button>(metrics).Count());
                Assert.Null(page.FindName("GoalRecentInvestmentCard"));
                Assert.Null(page.FindName("GoalInvestmentTrendButton"));
                Assert.Null(page.FindName("GoalCompletedTasksButton"));
                Assert.Same(model.GoalInvestmentTrend.OpenCommand, total.Command);
                Assert.Same(model.GoalCompletedTasks.OpenCommand, count.Command);
                foreach (var (metric, name) in new[] { (total, "GoalInvestmentDetailChevron"), (count, "GoalCompletedDetailChevron") })
                {
                    var arrow = (System.Windows.Shapes.Path)page.FindName(name);
                    Assert.True(arrow.IsVisible);
                    Assert.False(arrow.IsHitTestVisible);
                    Assert.InRange(arrow.TranslatePoint(new Point(arrow.ActualWidth, arrow.ActualHeight), metric).X, 0, metric.ActualWidth);
                }
                var value = (TextBlock)page.FindName("GoalCompletedTaskCountText");
                Assert.Equal("13", value.Inlines.OfType<Run>().First().Text);
                foreach (var card in new[] { total, count })
                {
                    Assert.True(card.ActualWidth > 0);
                    Assert.NotNull(card.InputHitTest(new Point(card.ActualWidth / 2, card.ActualHeight / 2)));
                }
                Invoke(total); Pump(page);
                Assert.True(model.GoalInvestmentTrend.IsOpen);
                Assert.False(model.GoalCompletedTasks.IsOpen);
                model.GoalInvestmentTrend.CloseCommand.Execute(null);
                Invoke(count); Pump(page);
                Assert.True(model.GoalCompletedTasks.IsOpen);
                Assert.Equal("已完成 13 项", model.GoalCompletedTasks.SummaryDisplay);
                model.GoalCompletedTasks.CloseCommand.Execute(null);
                Save(page, "goal-details-collapsed");
                var history = (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl");
                var date = model.GoalFocusHistory.Days.Single(item => item.Date == day);
                Assert.Equal("1小时5分钟", date.SummaryDisplay);
                Assert.All(date.Sessions, session => Assert.False(session.IsTasksExpanded));
                date.ToggleCommand.Execute(null); Pump(page);
                Save(page, "goal-details-sessions");
                var rows = Descendants<Border>(history).Where(item => item.Name == "SessionListRow").ToArray();
                var withTasks = rows.Single(item => ((GoalFocusSessionViewModel)item.DataContext).Record == latest);
                var withoutTasks = rows.Single(item => ((GoalFocusSessionViewModel)item.DataContext).Record == plain);
                Assert.Empty(Descendants<Button>(withTasks));
                Assert.Equal(3, Descendants<Grid>(withTasks).Count(item => item.Name == "HistoryParentTask"));
                Assert.Equal(Visibility.Visible, Descendants<ItemsControl>(withTasks).Single(item => item.Name == "SessionTaskDetails").Visibility);
                Assert.Equal(Visibility.Collapsed, Descendants<ItemsControl>(withoutTasks).Single(item => item.Name == "SessionTaskDetails").Visibility);
                var metricsHeight = metrics.ActualHeight;
                var marks = Descendants<System.Windows.Shapes.Ellipse>(withTasks).Where(item => item.Name == "HistorySubTaskCircle").ToArray();
                Assert.Equal(2, marks.Length);
                Assert.All(marks, mark => Assert.Equal(((SolidColorBrush)page.FindResource("AccentPrimary")).Color, ((SolidColorBrush)mark.Fill).Color));
                Assert.Empty(Descendants<Grid>(history).Where(item => item.Name == "SessionGap"));
                Save(page, "goal-details-expanded");
                date.ToggleCommand.Execute(null); Pump(page);
                Assert.False(withTasks.IsVisible);
                date.ToggleCommand.Execute(null); Pump(page);
                Assert.True(withTasks.IsVisible);
                Assert.Equal(metricsHeight, metrics.ActualHeight);
                model.GoalCompletedTasks.ApplyState(goal, []); Pump(page);
                Assert.Equal("0", value.Inlines.OfType<Run>().First().Text);
                Assert.True(count.IsEnabled);
                Invoke(count); Pump(page);
                Assert.True(model.GoalCompletedTasks.IsOpen);
                Assert.Equal("已完成 0 项", model.GoalCompletedTasks.SummaryDisplay);
                Assert.Equal(3, latest.CompletedTaskSnapshots.Count);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Goal page refinement timed out.");
        if (failure is not null) throw new InvalidOperationException("Goal page refinement failed.", failure);
    }

    private static void Invoke(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Pump(FrameworkElement page)
    {
        page.UpdateLayout(); page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); page.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Save(FrameworkElement page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_GOAL_DETAILS_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
