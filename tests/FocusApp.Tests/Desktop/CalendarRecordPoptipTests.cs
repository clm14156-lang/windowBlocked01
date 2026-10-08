using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CalendarRecordPoptipTests
{
    [Fact]
    public void CompletedSnapshotHierarchyIsOrderedAndPreviewCountsParentsAndChildren()
    {
        var model = new CalendarRecordPoptipViewModel(SessionWithTasks());
        Assert.Equal(7, model.AllRows.Count);
        Assert.Equal(6, model.VisibleRows.Count);
        Assert.Equal("还有 1 项", model.MoreDisplay);
        Assert.Equal(new[] { "调整首页导航布局", "优化顶部样式", "修复交互问题", "修复时间选择 Bug",
            "更新选择器逻辑", "适配移动端样式", "完成验收" }, model.AllRows.Select(row => row.Name));
        Assert.Equal(new[] { false, true, true, false, true, true, false }, model.AllRows.Select(row => row.IsSubTask));
        Assert.True(model.AllRows[2].IsLastChild);
        Assert.True(model.AllRows[5].IsLastChild);
        model.ExpandCommand.Execute(null);
        Assert.Equal(7, model.VisibleRows.Count);
        Assert.Equal(168, model.TaskViewportHeight);
    }

    [Fact]
    public void GoalAndEmptyTaskStatesShrinkWithoutArtificialGoalOrTaskLabels()
    {
        Sta(() =>
        {
            var start = DateTime.Today.AddHours(19).AddMinutes(19);
            var bound = new CalendarFocusTimelineToolTip
            {
                DataContext = new FocusSessionRecordViewModel(start, start.AddMinutes(23), "goal", "产品开发", "", 0)
            };
            Layout(bound);
            var boundHeight = bound.DesiredSize.Height;
            Assert.Equal(Visibility.Visible, ((TextBlock)bound.FindName("GoalTitle")).Visibility);
            Assert.Equal("产品开发", ((TextBlock)bound.FindName("GoalTitle")).Text);
            Assert.Equal("19:19 - 19:42 · 23分钟", ((TextBlock)bound.FindName("TimeSummary")).Text);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)bound.FindName("CompletedContent")).Visibility);
            bound.DataContext = new FocusSessionRecordViewModel(start, start.AddMinutes(23), "goal-unassigned",
                "自由专注", "不应作为完成任务出现", 0);
            Layout(bound);
            Assert.Equal(Visibility.Collapsed, ((TextBlock)bound.FindName("GoalTitle")).Visibility);
            Assert.Empty(bound.PoptipModel!.AllRows);
            Assert.True(bound.DesiredSize.Height < boundHeight);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)bound.FindName("CompletedContent")).Visibility);
            // Legacy sessions with names but no detail snapshots remain readable.
            bound.DataContext = new FocusSessionRecordViewModel(start, start.AddMinutes(23), "", "", "旧任务", 1);
            Layout(bound);
            Assert.Equal("旧任务", Assert.Single(bound.PoptipModel!.VisibleRows).Name);
        });
    }

    [Fact]
    public void FixedWidthTrimsLongNamesAndCountsOnlyCompletedParents()
    {
        Sta(() =>
        {
            var longName = string.Concat(Enumerable.Repeat("Windows专注软件产品开发优化", 4));
            var start = DateTime.Today.AddHours(13);
            var record = new FocusSessionRecordViewModel(start, start.AddMinutes(24), "goal", longName, "", 0)
            {
                CompletedTaskSnapshots = [new("parent", longName, 0)
                    { Details = new("", [new(longName, true), new(longName, true)]) }]
            };
            Assert.Equal(1, record.CalendarCompletedTaskCount);
            Assert.Equal("1项", record.CalendarCompletedTaskCountDisplay);
            Assert.True(record.HasCalendarCompletedTasks);
            var view = new CalendarFocusTimelineToolTip { DataContext = record };
            Layout(view);
            Assert.Equal(200, view.ActualWidth);
            Assert.Equal(200, ((Border)view.FindName("PoptipSurface")).ActualWidth);
            var title = (TextBlock)view.FindName("GoalTitle");
            var names = Descendants<TextBlock>((ItemsControl)view.FindName("TaskRows")).Where(text => text.Name == "TaskName");
            Assert.All(names.Append(title), text =>
            {
                Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                Assert.InRange(text.ActualWidth, 1, 174);
                Assert.True(text.ActualHeight < 24);
            });
            var unbound = new FocusSessionRecordViewModel(start, start.AddMinutes(24), "", "", "", 0);
            Assert.False(unbound.HasCalendarCompletedTasks);
            unbound.CompletedTaskCount = 2;
            Assert.True(unbound.HasCalendarCompletedTasks);
            Assert.Equal("2项", unbound.CalendarCompletedTaskCountDisplay);
        });
    }

    [Fact]
    public void LimitedSpaceAboveAxisUsesWholeRowsAndAnAccurateRemainingCount()
    {
        Sta(() =>
        {
            var view = new CalendarFocusTimelineToolTip { DataContext = SessionWithTasks() };
            Layout(view);
            view.FitAboveTimeline(200);
            Layout(view);
            var scroll = (ScrollViewer)view.FindName("TaskScroll");
            var model = view.PoptipModel!;
            Assert.True(view.ActualHeight <= 200);
            Assert.InRange(model.VisibleRows.Count, 1, 5);
            Assert.Equal(model.VisibleRows.Count * 28, scroll.ActualHeight);
            Assert.Equal(model.AllRows.Count - model.VisibleRows.Count, model.RemainingCount);
            var height = view.ActualHeight;
            model.ExpandCommand.Execute(null);
            Layout(view);
            Assert.Equal(height, view.ActualHeight);
            Assert.Equal(7, model.VisibleRows.Count);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        });
    }

    [Fact]
    public void HoverCanEnterPoptipAndExpandedTasksScrollWithoutGrowingOrClearingSelection()
    {
        Sta(() =>
        {
            var record = SessionWithTasks();
            var model = new StatisticsOverviewViewModel(false);
            model.SetUserAccess(true, true);
            model.SelectCalendarCommand.Execute(null);
            model.FocusSessionRecords.Add(record);
            model.ReturnToTodayCommand.Execute(null);
            var page = new StatisticsPage { DataContext = model };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                page.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            var window = new Window { Width = 900, Height = 740, Content = page, ShowActivated = false,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000 };
            try
            {
                window.Show();
                Pump(page);
                var timeline = (CalendarFocusTimeline)page.FindName("CalendarDayTimeline");
                var row = Descendants<Border>(page).Single(item => item.Name == "CalendarRecordRow");
                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Assert.Null(page.CalendarRecordInteraction.HoveredTimelineRecord);
                var segment = timeline.Timeline!.Segments.Single();
                var center = new Point((segment.StartRatio + segment.WidthRatio / 2) * timeline.ActualWidth,
                    timeline.TrackTop + timeline.TrackHeight / 2);
                typeof(CalendarFocusTimeline).GetMethod("UpdateSessionHover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, [center]);
                var host = typeof(CalendarFocusTimeline).GetField("_recordPoptip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!;
                var content = (CalendarFocusTimelineToolTip)host.GetType().GetProperty("Content")!.GetValue(host)!;
                bool IsOpen() => (bool)host.GetType().GetProperty("IsOpen")!.GetValue(host)!;
                Assert.True(IsOpen());
                typeof(CalendarFocusTimeline).GetMethod("UpdateSessionHover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timeline, [new Point()]);
                content.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                WaitForLeave(page);
                Assert.True(IsOpen());
                content.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                WaitForLeave(page);
                Assert.False(IsOpen());

                void ClickRow() => row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                ClickRow();
                Pump(page);
                Assert.Same(record, page.CalendarRecordInteraction.SelectedRecord);
                Assert.Same(PresentationSource.FromVisual(page), PresentationSource.FromVisual(content));
                var initialHeight = content.ActualHeight;
                var taskScroll = (ScrollViewer)content.FindName("TaskScroll");
                var items = (ItemsControl)content.FindName("TaskRows");
                var more = (Button)content.FindName("MoreLink");
                var hit = window.InputHitTest(more.TranslatePoint(
                    new Point(more.ActualWidth / 2, more.ActualHeight / 2), window)) as DependencyObject;
                Assert.NotNull(hit);
                while (hit is not null && !ReferenceEquals(hit, content)) hit = VisualTreeHelper.GetParent(hit);
                Assert.Same(content, hit);
                Assert.Equal(6, items.Items.Count);
                Assert.InRange(taskScroll.ActualHeight, 1, 168);
                Assert.Equal(ScrollBarVisibility.Disabled, taskScroll.VerticalScrollBarVisibility);
                var icons = Descendants<Ellipse>(items).ToArray();
                Assert.Equal(6, icons.Length);
                Assert.NotEqual(Colors.White, ((SolidColorBrush)icons[0].Fill).Color);
                Assert.Equal(Colors.White, ((SolidColorBrush)icons[1].Fill).Color);
                Assert.Equal(Visibility.Visible, Descendants<Grid>(items).First(item => item.Name == "HierarchyLine" &&
                    item.DataContext is CalendarPoptipTaskRow { IsSubTask: true }).Visibility);
                more.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.Same(record, page.CalendarRecordInteraction.SelectedRecord);
                typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(more, null);
                Pump(page);
                Assert.True(IsOpen());
                Assert.Equal(initialHeight, content.ActualHeight);
                Assert.Equal(7, items.Items.Count);
                Assert.Equal(ScrollBarVisibility.Auto, taskScroll.VerticalScrollBarVisibility);
                taskScroll.ScrollToBottom();
                Pump(page);
                Assert.True(taskScroll.VerticalOffset > 0);
                Assert.True(taskScroll.ExtentHeight > taskScroll.ViewportHeight);
                content.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                WaitForLeave(page);
                Assert.True(IsOpen());
                Assert.Same(record, page.CalendarRecordInteraction.SelectedRecord);
                // Repeated close/reopen must detach the old visual/logical parent.
                for (var index = 0; index < 3; index++)
                {
                    page.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    Assert.False(IsOpen());
                    Assert.Null(page.CalendarRecordInteraction.SelectedRecord);
                    ClickRow();
                    Pump(page);
                    Assert.True(IsOpen());
                }
            }
            finally { window.Close(); }
        });
    }

    private static FocusSessionRecordViewModel SessionWithTasks()
    {
        var start = DateTime.Today.AddHours(19).AddMinutes(19);
        return new(start, start.AddMinutes(23), "goal", "产品开发", "旧标题", 3)
        {
            CompletedTaskSnapshots =
            [
                new("3", "完成验收", 2),
                new("1", "调整首页导航布局", 0) { Details = new("", [
                    new("优化顶部样式", true), new("修复交互问题", true), new("未完成子任务", false)]) },
                new("2", "修复时间选择 Bug", 1) { Details = new("", [
                    new("更新选择器逻辑", true), new("适配移动端样式", true)]) }
            ]
        };
    }
    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(304, double.PositiveInfinity));
        view.Arrange(new Rect(new Point(), view.DesiredSize));
        Pump(view);
    }
    private static void Pump(FrameworkElement view)
    {
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
    }
    private static void WaitForLeave(FrameworkElement page) { Thread.Sleep(190); Pump(page); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Calendar poptip verification failed.", failure);
    }
}
