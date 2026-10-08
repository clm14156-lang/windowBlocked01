using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalFocusHistoryPresentationTests
{
    [Fact]
    public void HistoryExpansionKeepsTheHeaderAndViewportFixedAndLongReadOnlyResultsWrapAndScroll()
    {
        RunSta(() =>
        {
            var today = new DateTime(2026, 10, 4);
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => today.AddHours(23));
            model.SetUserAccess(true, true);
            var goal = new GoalOverviewItemViewModel("dev", "开发屏蔽软件", "", "", false, false);
            model.Goals.Add(goal);
            model.SelectGoalsCommand.Execute(null);
            model.SelectGoalCommand.Execute(goal);
            var description = string.Concat(Enumerable.Repeat("统一自动开始、手动切换与异常状态下的规则判断逻辑，避免误触发与漏拦截。", 10));
            var tasks = Enumerable.Range(0, 20).Select(index => new LocalFocusSessionTaskSnapshotDto($"t{index}", $"历史任务 {index + 1}", index)
            {
                Details = new LocalTaskDetailsSnapshotDto(index == 0 ? description : "",
                    index == 0 ? Enumerable.Range(0, 12).Select(child => new LocalSubTaskSnapshotDto($"子任务 {child + 1}", true)).ToArray() : [])
            }).ToArray();
            model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(19), today.AddHours(20), "dev", goal.Name, "", 20, tasks.Select(task => task.TaskNameSnapshot))
            {
                CompletedTaskIds = tasks.Select(task => task.TaskId).ToArray(), CompletedTaskSnapshots = tasks
            });
            for (var index = 0; index < 12; index++)
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddHours(8).AddMinutes(index * 15), today.AddHours(8).AddMinutes(index * 15 + 10), "dev", goal.Name, "", 0));
            model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(today.AddDays(-1).AddHours(10), today.AddDays(-1).AddHours(11), "dev", goal.Name, "", 0));
            var page = new StatisticsPage { DataContext = model };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            Layout(page);
            var header = (FrameworkElement)page.FindName("GoalDetailHeader");
            var metrics = (FrameworkElement)page.FindName("GoalInvestmentMetrics");
            var history = (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl");
            var scroll = (ScrollViewer)history.FindName("HistoryScrollViewer");
            var headerHeight = header.ActualHeight;
            var metricsHeight = metrics.ActualHeight;
            foreach (var name in new[] { "GoalRecentInvestmentText", "GoalTotalInvestmentText" })
            {
                var value = (TextBlock)page.FindName(name);
                var bounds = value.TransformToAncestor(metrics).TransformBounds(new Rect(0, 0, value.ActualWidth, value.ActualHeight));
                Assert.InRange(bounds.Bottom, 0, metrics.ActualHeight);
            }
            var width = scroll.ViewportWidth;
            Assert.All(model.GoalFocusHistory.Days, day => Assert.False(day.IsExpanded));
            Assert.True(width > 400);

            model.GoalFocusHistory.Days[0].ToggleCommand.Execute(null);
            model.GoalFocusHistory.Days[1].ToggleCommand.Execute(null);
            model.GoalFocusHistory.Days[0].Sessions[0].ToggleTasksCommand.Execute(null);
            Layout(page);
            Assert.True(model.GoalFocusHistory.Days[0].IsExpanded);
            Assert.True(model.GoalFocusHistory.Days[1].IsExpanded);
            Assert.True(scroll.ScrollableHeight > scroll.ViewportHeight);
            Assert.Equal(width, scroll.ViewportWidth, 3);
            Assert.Equal(headerHeight, header.ActualHeight, 3);
            Assert.Equal(metricsHeight, metrics.ActualHeight, 3);
            Assert.Empty(Descendants<TextBox>(history));
            Assert.Empty(Descendants<CheckBox>(history));
            Assert.Empty(Descendants<FrameworkElement>(history).Where(element => element.ContextMenu is not null));
            var note = Assert.Single(Descendants<TextBlock>(history).Where(text => text.Text == description));
            Assert.Equal(TextWrapping.Wrap, note.TextWrapping);
            Assert.True(note.ActualHeight > note.FontSize * 2);
            Assert.True(note.ActualWidth <= width);
            Assert.Equal(12, Descendants<TextBlock>(history).Count(text => text.DataContext is GoalHistorySubTaskViewModel child && text.Text == child.Title));
            Assert.Equal(FontWeights.Medium, Descendants<TextBlock>(history).Single(text => text.Text == model.GoalFocusHistory.Days[0].DateDisplay).FontWeight);
            scroll.ScrollToBottom();
            Layout(page);
            Assert.True(scroll.VerticalOffset > 0);
            Assert.Equal(headerHeight, header.ActualHeight, 3);
            model.FocusSessionRecords.Clear();
            Layout(page);
            Assert.False(model.GoalFocusHistory.HasRecords);
            Assert.True(model.GoalFocusHistory.ShowEmptyState);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)history.FindName("GoalFocusHistoryEmptyState")).Visibility);
            Assert.Single(Descendants<TextBlock>(history).Where(text => text.Text == "还没有专注记录"));
        });
    }

    [Fact]
    public void CompletedTaskEntryKeepsItsActionAndHistoryNoLongerRendersATimeline()
    {
        RunSta(() =>
        {
            var day = new DateTime(2026, 10, 4);
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => day);
            model.SetUserAccess(true, true);
            var goal = new GoalOverviewItemViewModel("dev", "开发屏蔽软件", "", "", false, false);
            model.Goals.Add(goal); model.SelectGoalsCommand.Execute(null); model.SelectGoalCommand.Execute(goal);
            foreach (var hour in new[] { 0, 10, 23 })
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(day.AddHours(hour), day.AddHours(hour).AddMinutes(30), "dev", goal.Name, "", hour == 10 ? 1 : 0, hour == 10 ? ["历史任务"] : []));
            var page = CreatePage(model);
            var completed = (Button)page.FindName("GoalCompletedTasksButton");
            Assert.Same(((Button)page.FindName("GoalInvestmentTrendButton")).Style, completed.Style);
            Assert.Same(model.GoalCompletedTasks.OpenCommand, completed.Command);
            completed.Command.Execute(null); Layout(page);
            Assert.True(model.GoalCompletedTasks.IsOpen);
            Assert.All(model.GoalFocusHistory.Days, item => Assert.False(item.IsExpanded));
            model.GoalFocusHistory.ShowCompletedTasks(); Layout(page);
            var history = (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl");
            Assert.Empty(Descendants<CalendarFocusTimeline>(history));
            Assert.Empty(Descendants<Border>(history).Where(border => border.Name == "SessionConnector"));
            Assert.True(model.GoalFocusHistory.Days[0].IsExpanded);
            Assert.True(model.GoalFocusHistory.Days[0].Sessions.Single(item => item.HasTasks).IsTasksExpanded);
            Assert.Equal(3, Descendants<Border>(history).Count(border => border.Name == "SessionListRow"));
        });
    }

    [Fact]
    public void SessionRowsShowZeroOneAndManyTasksAndExpandOnlyTheChosenRecord()
    {
        RunSta(() =>
        {
            var day = new DateTime(2026, 10, 4);
            var model = new StatisticsOverviewViewModel(false, localNowProvider: () => day);
            model.SetUserAccess(true, true);
            var goal = new GoalOverviewItemViewModel("dev", "开发屏蔽软件", "", "", false, false);
            model.Goals.Add(goal); model.SelectGoalsCommand.Execute(null); model.SelectGoalCommand.Execute(goal);
            foreach (var (hour, count) in new[] { (19, 1), (18, 3), (14, 0) })
            {
                var start = day.AddHours(hour);
                var snapshots = Enumerable.Range(0, count).Select(index => new LocalFocusSessionTaskSnapshotDto($"{hour}-{index}", $"历史任务 {hour}-{index}", index)
                {
                    CompletedAtUtc = new DateTimeOffset(start.AddMinutes(10 + index * 5)).ToUniversalTime(),
                    Details = new LocalTaskDetailsSnapshotDto("", index == 0 ? [new("调整信息层级", true), new("优化按钮位置", true)] : [])
                }).ToArray();
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(start, start.AddMinutes(30), "dev", goal.Name, "", count, snapshots.Select(item => item.TaskNameSnapshot))
                { SessionId = Guid.NewGuid(), CompletedTaskIds = snapshots.Select(item => item.TaskId).ToArray(), CompletedTaskSnapshots = snapshots });
            }
            model.GoalFocusHistory.Days[0].ToggleCommand.Execute(null);
            var page = CreatePage(model);
            var window = new Window { Width = 726, Height = 676, Content = page, WindowStyle = WindowStyle.None,
                ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            window.Show(); Layout(page);
            try
            {
            var history = (GoalFocusHistoryView)page.FindName("GoalFocusHistoryControl");
            var rows = Descendants<Border>(history).Where(border => border.Name == "SessionListRow").ToArray();
            Assert.Equal(3, rows.Length);
            Assert.Empty(Descendants<CalendarFocusTimeline>(history));
            foreach (var row in rows)
            {
                var session = (GoalFocusSessionViewModel)row.DataContext;
                var header = Descendants<Button>(row).Single(button => button.Name == "SessionSummaryButton");
                var summary = Descendants<TextBlock>(row).Single(text => text.Name == "SessionTaskSummary");
                var details = Descendants<TextBlock>(row).Single(text => text.Name == "SessionDetails");
                var pill = Descendants<Border>(row).Single(border => border.Name == "SessionTaskPill");
                Assert.Equal(session.HasTasks, header.IsEnabled);
                Assert.Equal(session.HasTasks ? Visibility.Visible : Visibility.Collapsed, pill.Visibility);
                Assert.Equal(session.HasTasks ? Visibility.Visible : Visibility.Collapsed, summary.Visibility);
                Assert.Equal(session.SessionDetailsDisplay, details.Text);
                Assert.Equal(session.TaskSummaryDisplay, summary.Text);
                Assert.Single(Descendants<System.Windows.Shapes.Ellipse>(row));
                if (session.HasTasks)
                {
                    Assert.Equal(session.TaskCountDisplay, Assert.Single(Descendants<TextBlock>(pill)).Text);
                    var hitPoint = pill.TranslatePoint(new Point(pill.ActualWidth / 2, pill.ActualHeight / 2), header);
                    Assert.NotNull(header.InputHitTest(hitPoint));
                }
                else
                {
                    Assert.Equal("14:00 - 14:30 · 30 分钟", details.Text);
                    Assert.Empty(session.TaskToggleDisplay);
                }
            }
            var fixedHeader = (FrameworkElement)page.FindName("GoalDetailHeader");
            var metrics = (FrameworkElement)page.FindName("GoalInvestmentMetrics");
            var headerHeight = fixedHeader.ActualHeight;
            var metricsHeight = metrics.ActualHeight;
            var before = rows[1].TranslatePoint(new Point(), history).Y;
            var firstHeight = rows[0].ActualHeight;
            Invoke(Descendants<Button>(rows[0]).Single()); Layout(page);
            Assert.True(model.GoalFocusHistory.Days[0].Sessions[0].IsTasksExpanded);
            Assert.False(model.GoalFocusHistory.Days[0].Sessions[1].IsTasksExpanded);
            Assert.True(rows[0].ActualHeight > firstHeight);
            Assert.InRange(Math.Abs(rows[1].TranslatePoint(new Point(), history).Y - before - (rows[0].ActualHeight - firstHeight)), 0, 1);
            Invoke(Descendants<Button>(rows[1]).Single()); Layout(page);
            var parents = Descendants<Grid>(rows[1]).Where(grid => grid.Name == "HistoryParentTask").ToArray();
            Assert.Equal(3, parents.Length);
            foreach (var parent in parents)
            {
                var task = (GoalHistoryTaskViewModel)parent.DataContext;
                var time = Descendants<TextBlock>(parent).Single(text => text.Name == "HistoryTaskCompletionTime");
                Assert.Equal(task.CompletedTimeDisplay, time.Text);
                Assert.Matches("^[0-9]{2}:[0-9]{2}$", time.Text);
                var name = Descendants<TextBlock>(parent).Single(text => text.Text == task.Name);
                Assert.True(time.TranslatePoint(new Point(), parent).X > name.TranslatePoint(new Point(), parent).X);
                var mark = Descendants<Border>(parent).Single(border => border.Name == "HistoryParentCompletionMark");
                Assert.Equal(mark.ActualWidth, mark.ActualHeight);
                Assert.Equal(new CornerRadius(3), mark.CornerRadius);
                Assert.False(mark.IsHitTestVisible);
                Assert.Equal(Colors.White, ((SolidColorBrush)Assert.Single(Descendants<System.Windows.Shapes.Path>(mark)).Stroke).Color);
                if (task.HasSubTasks)
                {
                    var children = Descendants<TextBlock>(parent).Where(text => text.DataContext is GoalHistorySubTaskViewModel child && text.Text == child.Title).ToArray();
                    Assert.Equal(2, children.Length);
                    Assert.All(children, child => Assert.True(child.TranslatePoint(new Point(), parent).X > name.TranslatePoint(new Point(), parent).X));
                    var childRows = Descendants<Grid>(parent).Where(grid => grid.Name == "HistorySubTask").ToArray();
                    Assert.Equal(2, childRows.Length);
                    foreach (var row in childRows)
                    {
                        var circle = Assert.Single(Descendants<System.Windows.Shapes.Ellipse>(row));
                        Assert.Equal(0, ((SolidColorBrush)circle.Fill).Color.A);
                        Assert.Equal(((SolidColorBrush)mark.Background).Color, ((SolidColorBrush)circle.Stroke).Color);
                        Assert.Single(Descendants<System.Windows.Shapes.Path>(row));
                        var bottom = Descendants<Border>(row).Single(border => border.Name == "TaskHierarchySpineBottom");
                        Assert.Equal(((GoalHistorySubTaskViewModel)row.DataContext).IsLast ? Visibility.Collapsed : Visibility.Visible, bottom.Visibility);
                        Assert.Equal(1, Descendants<Border>(row).Single(border => border.Name == "TaskHierarchyBranch").Height);
                    }
                    Assert.Empty(Descendants<TextBlock>(childRows[0]).Where(text => text.Name == "HistoryTaskCompletionTime"));
                }
                else Assert.Empty(Descendants<Grid>(parent).Where(grid => grid.Name == "HistorySubTask"));
            }
            Invoke(Descendants<Button>(rows[0]).Single()); Layout(page);
            Assert.False(model.GoalFocusHistory.Days[0].Sessions[0].IsTasksExpanded);
            Assert.True(model.GoalFocusHistory.Days[0].Sessions[1].IsTasksExpanded);
            Assert.Equal(headerHeight, fixedHeader.ActualHeight);
            Assert.Equal(metricsHeight, metrics.ActualHeight);
            }
            finally { window.Close(); }
        });
    }

    private static StatisticsPage CreatePage(StatisticsOverviewViewModel model)
    {
        var page = new StatisticsPage { DataContext = model };
        foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
            page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
        Layout(page);
        return page;
    }

    private static void Invoke(Button button)
    {
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(726, 676));
        view.Arrange(new Rect(0, 0, 726, 676));
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Goal history presentation timed out.");
        if (failure is not null) throw new InvalidOperationException("Goal history presentation failed.", failure);
    }
}
