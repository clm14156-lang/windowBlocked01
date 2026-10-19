using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalCompletedTasksRedesignTests
{
    private static readonly DateTime Today = new(2027, 1, 3, 12, 0, 0);
    private static GoalOverviewItemViewModel Goal(string id = "g") => new(id, "目标", "", "", false, false);
    private static LocalTaskDto Completed(string id, DateTime? time, int sortOrder = 0) =>
        new(id, "g", "任务" + id, true, sortOrder, Local(Today.AddDays(-30)), Local(Today)) { CompletedAtUtc = time is { } value ? Local(value) : null };
    private static DateTimeOffset Local(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Local));
    private static string[] Ids(GoalCompletedTasksViewModel model) => model.Tasks.Select(row => row.Source.TaskId).ToArray();

    [Fact]
    public void RangesUseLocalMidnightMondayAndCalendarMonthAcrossTheYearBoundary()
    {
        var model = new GoalCompletedTasksViewModel(() => Today);
        LocalTaskDto[] tasks =
        [
            Completed("prior-week", new(2026, 12, 27, 23, 59, 59)),
            Completed("week-start", new(2026, 12, 28)),
            Completed("prior-month", new(2026, 12, 31, 23, 59, 59)),
            Completed("month-start", new(2027, 1, 1)),
            Completed("today-start", Today.Date), Completed("today", Today),
            Completed("next-week", Today.Date.AddDays(1)), Completed("unknown", null)
        ];
        model.ApplyState(Goal(), tasks.Concat([tasks[0], Completed("pending", Today) with { IsCompleted = false }, Completed("other-goal", Today) with { TargetId = "other" }]).ToArray());
        model.OpenCommand.Execute(null);
        Assert.True(model.IsAllRange); Assert.True(model.IsSortDescending); Assert.Equal(8, model.TotalCount); Assert.Equal(8, model.FilteredCount);
        model.IsTodayRange = true;
        Assert.Equal(new[] { "today", "today-start" }, Ids(model));
        model.IsWeekRange = true;
        Assert.Equal(new[] { "today", "today-start", "month-start", "prior-month", "week-start" }, Ids(model));
        model.IsMonthRange = true;
        Assert.Equal(new[] { "next-week", "today", "today-start", "month-start" }, Ids(model));
        Assert.Equal("共 4 项", model.FilteredCountDisplay);
        Assert.Equal("已完成 8 项", model.SummaryDisplay);
        Assert.Equal(8, model.TotalCount);
    }

    [Fact]
    public void LeapMonthAndExactTimestampSortKeepUnknownTimesLastWithoutInventingCompletionDates()
    {
        var day = new DateTime(2028, 2, 29, 12, 0, 0);
        var model = new GoalCompletedTasksViewModel(() => day);
        model.ApplyState(Goal(), [Completed("late", day.AddSeconds(1)), Completed("early", day), Completed("february", new(2028, 2, 1)),
            Completed("january", new(2028, 1, 31, 23, 59, 59)), Completed("march", new(2028, 3, 1)), Completed("unknown", null)]);
        model.IsMonthRange = true;
        Assert.Equal(new[] { "late", "early", "february" }, Ids(model));
        model.ToggleSortCommand.Execute(null);
        Assert.Equal(new[] { "february", "early", "late" }, Ids(model)); Assert.True(model.IsMonthRange);
        model.IsAllRange = true;
        Assert.Equal("unknown", Ids(model).Last());
        Assert.Equal("—", model.Tasks.Last().TimeDisplay);
        model.ToggleSortCommand.Execute(null); Assert.Equal("unknown", Ids(model).Last());
        Assert.Equal("2月29日 12:00", model.Tasks.Single(row => row.Source.TaskId == "late").TimeDisplay);
    }

    [Fact]
    public void StateRefreshRetainsTheActiveRangeButReopeningAndChangingGoalsResetToAll()
    {
        var clock = Today;
        var model = new GoalCompletedTasksViewModel(() => clock);
        var source = Completed("a", Today);
        model.ApplyState(Goal(), [source]); model.OpenCommand.Execute(null);
        model.IsTodayRange = true; model.IsSortDescending = false;
        model.ApplyState(Goal(), [source, Completed("b", Today.AddDays(-1))]);
        Assert.True(model.IsTodayRange); Assert.True(model.IsSortAscending); Assert.Equal("a", Assert.Single(model.Tasks).Source.TaskId);
        clock = Today.AddDays(1);
        model.CloseCommand.Execute(null); model.OpenCommand.Execute(null);
        Assert.True(model.IsAllRange); Assert.True(model.IsSortDescending); Assert.Equal(2, model.FilteredCount);
        model.IsTodayRange = true; Assert.Empty(model.Tasks); Assert.False(model.HasResults);
        model.ApplyState(Goal("other"), [source]); Assert.False(model.IsOpen); Assert.True(model.IsAllRange); Assert.Equal(0, model.TotalCount);
        model.ApplyState(null, [source]); Assert.False(model.OpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestoreAndDeleteKeepFullSourceDetailsAndRefreshTheFilteredAndOverallCounts()
    {
        var child = new LocalSubTaskDto("child", "a", "保留子任务", true, 0, Local(Today), Local(Today));
        var source = Completed("a", Today) with { Description = "保留备注", SubTasks = [child] };
        var model = new GoalCompletedTasksViewModel(() => Today);
        model.ApplyState(Goal(), [source, Completed("b", Today.AddDays(-1))]); model.IsTodayRange = true;
        var entry = Assert.Single(model.Tasks); var pending = new TaskCompletionSource<bool>();
        model.MutationHandler = (task, delete) => { Assert.Same(source, task); Assert.False(delete); Assert.Equal("保留备注", task.Description); Assert.Same(child, Assert.Single(task.SubTasks)); return pending.Task; };
        var operation = model.MutateAsync(entry, false);
        Assert.True(model.IsBusy); Assert.False(entry.RestoreCommand.CanExecute(entry)); Assert.False(await model.MutateAsync(entry, true));
        model.IsWeekRange = true; pending.SetResult(true); Assert.True(await operation);
        Assert.True(model.IsWeekRange); Assert.Equal(1, model.TotalCount); Assert.Equal(1, model.FilteredCount);
        model.MutationHandler = (_, _) => Task.FromResult(false);
        Assert.False(await model.MutateAsync(model.Tasks[0], true)); Assert.True(model.HasError); Assert.Equal(1, model.FilteredCount);
        model.MutationHandler = (_, delete) => Task.FromResult(delete);
        Assert.True(await model.MutateAsync(model.Tasks[0], true)); Assert.False(model.HasResults); Assert.Equal(0, model.TotalCount);
    }

    [Fact]
    public async Task SearchComposesWithRangeAndSortAndRetainsTheUnderlyingTaskAndOverallTotal()
    {
        var model = new GoalCompletedTasksViewModel(() => Today);
        var recent = Completed("recent", Today) with { Name = "实现 API 登录", Description = "不会修改备注" };
        var older = Completed("older", Today.AddDays(-1)) with { Name = "优化 api 文档" };
        var hidden = Completed("hidden", Today) with { Name = "整理房间", Description = "API" };
        var unknown = Completed("unknown", null) with { Name = "旧版 API 任务" };
        model.ApplyState(Goal(), [recent, older, hidden, unknown]); model.OpenCommand.Execute(null);
        model.SearchQuery = "  aPi  ";
        Assert.Equal(new[] { "recent", "older", "unknown" }, Ids(model)); Assert.Equal(4, model.TotalCount);
        model.IsTodayRange = true; Assert.Equal("recent", Assert.Single(Ids(model)));
        model.IsAllRange = true; model.ToggleSortCommand.Execute(null);
        Assert.Equal(new[] { "older", "recent", "unknown" }, Ids(model));
        model.ApplyState(Goal(), [recent, older, hidden, unknown]);
        Assert.Equal("  aPi  ", model.SearchQuery); Assert.True(model.IsSortAscending);
        model.MutationHandler = (source, delete) => { Assert.Same(older, source); Assert.False(delete); return Task.FromResult(true); };
        Assert.True(await model.MutateAsync(model.Tasks[0], false));
        Assert.Equal(3, model.TotalCount); Assert.Equal("共 2 项", model.FilteredCountDisplay);
        model.SearchQuery = "不存在"; Assert.False(model.HasResults); Assert.Equal("没有匹配的任务", model.EmptyText);
        model.SearchQuery = "   "; Assert.Equal(3, model.FilteredCount);
        model.SearchQuery = "登录"; Assert.Same(recent, Assert.Single(model.Tasks).Source);
        model.CloseCommand.Execute(null); model.OpenCommand.Execute(null);
        Assert.Equal("", model.SearchQuery); Assert.True(model.IsAllRange); Assert.True(model.IsSortDescending);
        model.SearchQuery = "登录"; model.ApplyState(Goal("other"), []);
        Assert.Equal("", model.SearchQuery); Assert.False(model.IsOpen);
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(900, 760)]
    public void NativeModalFiltersSortsAndVirtualizesAFlatListWithFixedHeaders(int width, int height)
    {
        Sta(() =>
        {
            var today = new DateTime(2026, 10, 16, 12, 0, 0);
            var tasks = Enumerable.Range(0, 240).Select(index => Completed(index.ToString("D3"), today.AddDays(-(index % 30)).AddSeconds(index))).ToArray();
            var model = new GoalCompletedTasksViewModel(() => today); model.ApplyState(Goal(), tasks); model.OpenCommand.Execute(null);
            var view = new GoalCompletedTasksModal { DataContext = model };
            foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" }) view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
            var window = new Window { Width = width, Height = height, Content = view, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try
            {
                window.Show(); Pump(view);
                var surface = (Border)view.FindName("ModalSurface"); var list = (ListBox)view.FindName("TaskList");
                var scroll = (ScrollViewer)list.Template.FindName("TasksScrollViewer", list);
                Assert.Equal(460, surface.ActualWidth); Assert.Equal(500, surface.ActualHeight);
                Assert.Equal((width - 460) / 2.0, Bounds(surface, view).Left, 1); Assert.InRange(Bounds(surface, view).Bottom, 1, height - 1);
                Assert.Equal("已完成的任务", ((TextBlock)view.FindName("CompletedTasksTitle")).Text);
                var search = (TextBox)view.FindName("SearchInput");
                var range = (ComboBox)view.FindName("RangeFilter");
                Assert.Null(view.FindName("DateGroups")); Assert.Empty(Descendants<RadioButton>(view));
                Assert.Equal(32, ((Border)view.FindName("CompletedSearchSurface")).ActualHeight);
                Assert.Equal(32, range.ActualHeight);
                Assert.True(Bounds(range, view).Right < Bounds(search, view).Left);
                Assert.True(Bounds(search, view).Right < Bounds((TextBlock)view.FindName("CompletedTaskCount"), view).Left);
                Assert.Equal(240, list.Items.Count); Assert.True(scroll.ScrollableHeight > 0);
                Assert.InRange(Descendants<Grid>(list).Count(grid => grid.Name == "CompletedTaskRow"), 8, 35);
                Assert.DoesNotContain(Descendants<Button>(view), button => button.Name == "DateGroupHeader");
                var header = (Border)view.FindName("CompletedTasksTableHeader"); var headerBounds = Bounds(header, view);
                scroll.ScrollToBottom(); Pump(view); Assert.True(scroll.VerticalOffset > 0); Assert.Equal(headerBounds, Bounds(header, view));
                Select(CompletedTaskTimeRange.Today); Assert.Equal(8, model.FilteredCount); Assert.True(model.IsTodayRange); Assert.InRange(scroll.VerticalOffset, 0, 1);
                Select(CompletedTaskTimeRange.Week); Assert.Equal(40, model.FilteredCount);
                Select(CompletedTaskTimeRange.Month); Assert.Equal(128, model.FilteredCount);
                Select(CompletedTaskTimeRange.All); Assert.Equal("共 240 项", ((TextBlock)view.FindName("CompletedTaskCount")).Text);
                var first = model.Tasks[0].Source.TaskId;
                ((Button)view.FindName("CompletionSortButton")).Command.Execute(null); Pump(view);
                Assert.True(model.IsSortAscending); Assert.NotEqual(first, model.Tasks[0].Source.TaskId);
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var row = Descendants<Grid>(container).Single(grid => grid.Name == "CompletedTaskRow");
                var title = Descendants<TextBlock>(row).Single(block => block.Name == "CompletedTaskName"); var time = Descendants<TextBlock>(row).Single(block => block.Name == "CompletedTaskTime");
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming); Assert.True(Bounds(title, row).Right <= Bounds(time, row).Left);
                Assert.Equal(model.Tasks[0].TimeDisplay, time.Text); Assert.Equal(40, container.Height);
                // Native popup coordinates must be checked with the modal on screen;
                // off-screen windows cause WPF to reposition popups onto the monitor.
                var workArea = SystemParameters.WorkArea;
                window.Left = workArea.Left + (workArea.Width - window.ActualWidth) / 2;
                window.Top = workArea.Top + (workArea.Height - window.ActualHeight) / 2;
                Pump(view);
                var more = Descendants<Button>(row).Single(button => button.Name == "CompletedTaskMoreButton");
                var rowSurface = (Border)container.Template.FindName("CompletedTaskRowSurface", container);
                var titleBounds = Bounds(title, row);
                Assert.True(time.IsVisible); Assert.False(more.IsVisible);
                Hover(container, true); Pump(view);
                Assert.False(time.IsVisible); Assert.True(more.IsVisible);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#F3F4F6"), ((SolidColorBrush)rowSurface.Background).Color);
                Assert.Equal(titleBounds, Bounds(title, row));
                Save(view, $"completed-tasks-{width}-hover");
                Hover(container, false); Pump(view);
                Assert.True(time.IsVisible); Assert.False(more.IsVisible);
                Hover(container, true); Invoke(more); Pump(view);
                Assert.Same(model.Tasks[0], container.ContextMenu.DataContext);
                var restore = (MenuItem)container.ContextMenu.Items[0]; Assert.Same(model.Tasks[0].RestoreCommand, restore.Command); Assert.Same(model.Tasks[0], restore.CommandParameter);
                var menu = container.ContextMenu;
                var actions = menu.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(new[] { "恢复任务", "删除任务" }, actions.Select(item => item.Header));
                Assert.All(actions, item =>
                {
                    var icon = Assert.IsType<System.Windows.Shapes.Path>(item.Icon);
                    Assert.NotNull(icon.Stroke); Assert.True(icon.IsVisible); Assert.True(icon.ActualWidth > 0);
                });
                Assert.Equal((Color)ColorConverter.ConvertFromString("#D92D45"), ((SolidColorBrush)actions[1].Foreground).Color);
                Assert.False(menu.StaysOpen);
                Hover(container, false); Pump(view);
                Assert.True(GoalCompletedTasksModal.GetIsTaskMenuOpen(container));
                Assert.False(time.IsVisible); Assert.True(more.IsVisible);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#F3F4F6"), ((SolidColorBrush)rowSurface.Background).Color);
                AssertMenuBounds(menu, surface, opensAbove: false);
                Save(menu, $"completed-tasks-{width}-menu");
                menu.IsOpen = false; Pump(view);
                Assert.False(GoalCompletedTasksModal.GetIsTaskMenuOpen(container));
                Assert.True(time.IsVisible); Assert.False(more.IsVisible);
                Hover(container, true); Invoke(more); Pump(view);
                var menuEscape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                view.RaiseEvent(menuEscape); Pump(view); Assert.False(menu.IsOpen); Assert.True(model.IsOpen);
                Hover(container, true); Invoke(more); Pump(view);
                model.IsTodayRange = true; Pump(view); Assert.False(menu.IsOpen);
                Select(CompletedTaskTimeRange.All);
                scroll.ScrollToBottom(); Pump(view);
                var bottom = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1);
                var bottomMore = Descendants<Button>(bottom).Single(button => button.Name == "CompletedTaskMoreButton");
                Hover(bottom, true); Invoke(bottomMore); Pump(view);
                AssertMenuBounds(bottom.ContextMenu, surface, opensAbove: true);
                scroll.ScrollToTop(); Pump(view); Assert.False(bottom.ContextMenu.IsOpen);
                search.Text = "任务00"; Pump(view);
                Assert.Equal("任务00", model.SearchQuery); Assert.Equal(10, model.FilteredCount);
                Assert.InRange(scroll.VerticalOffset, 0, 1); Assert.Equal(headerBounds, Bounds(header, view));
                Select(CompletedTaskTimeRange.Today); Assert.Equal(1, model.FilteredCount);
                search.Text = "无匹配任务"; Pump(view);
                Assert.True(((TextBlock)view.FindName("CompletedTasksEmptyState")).IsVisible);
                Assert.Equal("没有匹配的任务", ((TextBlock)view.FindName("CompletedTasksEmptyState")).Text);
                search.Text = ""; Pump(view); Assert.Equal(8, model.FilteredCount);
                range.IsDropDownOpen = true; Pump(view);
                var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                view.RaiseEvent(escape); Pump(view);
                Assert.False(range.IsDropDownOpen); Assert.True(model.IsOpen);
                var mutations = new List<bool>();
                model.MutationHandler = (_, delete) => { mutations.Add(delete); return Task.FromResult(true); };
                foreach (var delete in new[] { false, true })
                {
                    var actionRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var sourceId = ((GoalCompletedTaskEntryViewModel)actionRow.DataContext).Source.TaskId;
                    var actionMore = Descendants<Button>(actionRow).Single(button => button.Name == "CompletedTaskMoreButton");
                    Hover(actionRow, true); Invoke(actionMore); Pump(view);
                    var action = actionRow.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, delete ? "删除任务" : "恢复任务"));
                    Invoke(action); Pump(view);
                    Assert.DoesNotContain(model.Tasks, entry => entry.Source.TaskId == sourceId);
                    Assert.False(actionRow.ContextMenu.IsOpen);
                }
                Assert.Equal(new[] { false, true }, mutations);
                model.ApplyState(Goal(), []); Pump(view);
                Assert.True(((TextBlock)view.FindName("CompletedTasksEmptyState")).IsVisible); Assert.Equal("共 0 项", ((TextBlock)view.FindName("CompletedTaskCount")).Text);
                ((Button)view.FindName("CloseModalButton")).Command.Execute(null); Pump(view); Assert.False(view.IsVisible);

                void Select(CompletedTaskTimeRange selection)
                {
                    range.IsDropDownOpen = true; Pump(view);
                    var item = range.Items.Cast<ComboBoxItem>().Single(option => Equals(option.Tag, selection));
                    var optionPeer = new ComboBoxAutomationPeer(range).GetChildren()!.Single(peer => peer.GetName() == item.Content.ToString());
                    ((ISelectionItemProvider)optionPeer.GetPattern(PatternInterface.SelectionItem)!).Select(); Pump(view);
                    Assert.Equal(selection, model.SelectedRange);
                    Assert.False(range.IsDropDownOpen);
                }
            }
            finally { window.Close(); }
        });
    }

    private static void AssertMenuBounds(ContextMenu menu, FrameworkElement surface, bool opensAbove)
    {
        Assert.True(menu.IsOpen);
        var anchor = Assert.IsType<Button>(menu.PlacementTarget);
        Assert.Equal("CompletedTaskMoreButton", anchor.Name);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        var menuSurface = (Border)menu.Template.FindName("CompletedTaskMenuSurface", menu);
        var menuBounds = ScreenBounds(menuSurface);
        var anchorBounds = ScreenBounds(anchor);
        var modalBounds = ScreenBounds(surface);
        Assert.InRange(Math.Abs(menuBounds.Right - anchorBounds.Right), 0, 2);
        var gap = opensAbove ? anchorBounds.Top - menuBounds.Bottom : menuBounds.Top - anchorBounds.Bottom;
        Assert.InRange(gap, 0, 4 * VisualTreeHelper.GetDpi(anchor).DpiScaleY);
        Assert.True(modalBounds.Contains(menuBounds), $"Menu {menuBounds} must fit inside modal {modalBounds}.");
        Assert.True(modalBounds.Contains(ScreenBounds(menu)), "Popup including its shadow margin must fit inside the modal.");
    }
    private static Rect ScreenBounds(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point());
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }
    private static void Hover(UIElement element, bool value) => element.SetValue(
        (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static void Invoke(FrameworkElement element) => element.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(element, null);
    private static void Save(FrameworkElement element, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_COMPLETED_TASK_MENU_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(element), null, new Rect(new Size(element.ActualWidth, element.ActualHeight)));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    private static Rect Bounds(FrameworkElement item, UIElement relative) => new(item.TranslatePoint(new Point(), relative), new Size(item.ActualWidth, item.ActualHeight));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject { for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {var child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(var next in Descendants<T>(child))yield return next;} }
    private static void Pump(FrameworkElement view) { view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout(); }
    private static void Sta(Action action) {Exception? failure=null;var thread=new Thread(()=>{try{action();}catch(Exception error){failure=error;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(30)));if(failure is not null)throw new InvalidOperationException("Completed task redesign verification failed.",failure);}
}
