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
                    index == 0 ? Enumerable.Range(0, 12).Select(child => new LocalSubTaskSnapshotDto($"子任务 {child + 1}", child % 2 == 0)).ToArray() : [])
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
            Assert.Equal(12, Descendants<TextBlock>(history).Count(text => text.DataContext is GoalHistorySubTaskViewModel));
            Assert.Equal(FontWeights.SemiBold, Descendants<TextBlock>(history).Single(text => text.Text == model.GoalFocusHistory.Days[0].DateDisplay).FontWeight);
            scroll.ScrollToBottom();
            Layout(page);
            Assert.True(scroll.VerticalOffset > 0);
            Assert.Equal(headerHeight, header.ActualHeight, 3);
            model.FocusSessionRecords.Clear();
            Layout(page);
            Assert.False(model.GoalFocusHistory.HasRecords);
            Assert.Equal(Visibility.Visible, Descendants<TextBlock>(history).Single(text => text.Text == "暂无专注记录").Visibility);
        });
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
