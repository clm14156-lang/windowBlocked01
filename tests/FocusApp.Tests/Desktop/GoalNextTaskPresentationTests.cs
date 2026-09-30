using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalNextTaskPresentationTests
{
    [Fact]
    public void MenuAndInlineEditorsRenderWithoutChangingGoalPageAndActionsUseTheCurrentParent()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            ContextMenu? menu = null;
            try
            {
                var now = DateTimeOffset.Now;
                var parent = GoalNextTaskDetailsTests.TaskData("任务05") with { Description = "今天先完成数据结构，UI 晚点再处理。",
                    SubTasks = [new LocalSubTaskDto("sub1", "任务05", "任务07", false, 0, now, now),
                                new LocalSubTaskDto("sub2", "任务05", "任务08", false, 1, now, now)] };
                var state = GoalNextTaskDetailsTests.State(parent,
                    GoalNextTaskDetailsTests.TaskData("任务06", 1) with { Description = "研究一下相关方案，对比不同实现方式" },
                    GoalNextTaskDetailsTests.TaskData("优化一下 Windows 的数据布局", 2),
                    GoalNextTaskDetailsTests.TaskData("213", 3));
                var model = new StatisticsOverviewViewModel(false);
                model.ApplyState(state);
                model.SetUserAccess(true, true);
                model.SelectGoalsCommand.Execute(null);
                model.GoalTasks.PersistTaskDetailsAsync = change =>
                {
                    var command = GoalTaskPersistence.CreateDetailsCommand(state, change)!;
                    state = state with { Tasks = command.Tasks };
                    model.ApplyTaskState(state);
                    return Task.FromResult<LocalTaskDto?>(command.Tasks.Single(item => item.TaskId == change.TaskId));
                };
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 760, Height = 710, Content = page, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var card = (Border)page.FindName("GoalInvestmentDetailsCard");
                var width = card.ActualWidth;
                Assert.InRange(card.ActualHeight, page.ActualHeight - 64, page.ActualHeight - 62);
                var task = model.GoalTasks.PendingTasks[0];
                var row = Descendants<Grid>(page).First(element => element.Name == "GoalNextTaskRow");
                var more = Descendants<Button>(row).Single(element => element.Name == "GoalNextTaskMoreButton");
                Assert.True(more.IsVisible);
                var children = Descendants<ItemsControl>(row).Single(element => element.Name == "GoalTaskSubTasks");
                Assert.Equal(2, children.Items.Count);
                Assert.True(children.IsVisible);
                var titleLine = Descendants<Grid>(row).Single(element => element.MinHeight == 40);
                Assert.Equal(4, titleLine.ColumnDefinitions.Count);
                var titleLineHeight = titleLine.ActualHeight;
                var capsule = Descendants<Button>(row).Single(element => element.Name == "GoalTaskSubTasksToggle");
                Assert.True(capsule.IsVisible);
                SavePreview(page, "next-tasks");
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, more));
                Pump();
                menu = more.ContextMenu;
                Assert.True(menu.IsOpen);
                Assert.True(task.IsMenuOpen);
                var actions = menu.Items.Cast<MenuItem>().ToArray();
                Assert.Equal(new[] { "编辑任务", "编辑备注", "添加子任务", "删除任务" }, actions.Select(item => item.Header));
                Assert.All(actions, action => Assert.Same(task, action.CommandParameter));
                Assert.All(actions, action =>
                {
                    Assert.IsType<PathGeometry>(action.Icon);
                    var icon = (System.Windows.Shapes.Path)action.Template.FindName("MenuIcon", action);
                    Assert.True(icon.IsVisible);
                    Assert.NotNull(icon.Stroke);
                });
                SavePreview(page, "next-tasks-menu", menu, more);
                actions[0].Command.Execute(actions[0].CommandParameter);
                menu.IsOpen = false;
                Pump();
                var nameEditor = Descendants<TextBox>(row).Single(element => element.Name == "GoalTaskNameEditor");
                Assert.True(nameEditor.IsVisible);
                Assert.Equal(titleLineHeight, titleLine.ActualHeight);
                Assert.Equal(0, nameEditor.BorderThickness.Left);
                SavePreview(page, "next-task-name-editor");
                model.GoalTasks.CancelInlineEdit(task);
                Pump();
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, more));
                Pump();
                actions[1].Command.Execute(actions[1].CommandParameter);
                menu.IsOpen = false;
                Pump();
                var remarkEditor = Descendants<TextBox>(row).Single(element => element.Name == "GoalTaskRemarkEditor");
                Assert.True(remarkEditor.IsVisible);
                Assert.True(remarkEditor.AcceptsReturn);
                remarkEditor.Text = "备注支持多行\n刷新后仍然保留";
                Pump();
                SavePreview(page, "next-task-remark-editor");
                remarkEditor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, remarkEditor, more)
                    { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                Pump();
                Assert.Equal("备注支持多行\n刷新后仍然保留", task.Description);
                Assert.False(remarkEditor.IsVisible);
                Assert.True(Descendants<TextBlock>(row).Single(element => element.Name == "GoalTaskRemark").IsVisible);
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, more));
                Pump();
                actions[2].Command.Execute(actions[2].CommandParameter);
                menu.IsOpen = false;
                Pump();
                var subEditor = Descendants<TextBox>(row).Single(element => element.Name == "GoalTaskSubTaskEditor");
                Assert.True(subEditor.IsVisible);
                subEditor.Text = "调整任务列表位置";
                Pump();
                SavePreview(page, "next-task-subtask-editor");
                PressEnter(subEditor);
                Pump();
                Assert.Equal(3, children.Items.Count);
                Assert.Equal("", subEditor.Text);
                Assert.False(subEditor.IsVisible);
                var addSubTask = Descendants<Button>(row).Single(element => element.Name == "GoalAddSubTaskButton");
                Assert.True(addSubTask.IsVisible);
                Assert.Equal("调整任务列表位置", task.SubTasks.Last().Title);
                var childCheck = Descendants<Button>(children).Last();
                childCheck.Command.Execute(childCheck.CommandParameter);
                Pump();
                Assert.True(task.SubTasks.Last().IsCompleted);
                addSubTask.Command.Execute(addSubTask.CommandParameter);
                Pump();
                PressEnter(subEditor); // Empty Enter must not create another item.
                Assert.Equal(3, task.SubTasks.Count);
                model.GoalTasks.CancelInlineEdit(task);
                Pump();
                SavePreview(page, "next-tasks-saved");
                Assert.Equal(width, card.ActualWidth);
                Assert.InRange(card.ActualHeight, page.ActualHeight - 64, page.ActualHeight - 62);
                capsule.Command.Execute(capsule.CommandParameter);
                Pump();
                Assert.False(children.IsVisible);
                Assert.True(Descendants<TextBlock>(row).Single(element => element.Name == "GoalTaskRemark").IsVisible);
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, more));
                Pump();
                model.SelectOverviewCommand.Execute(null);
                Pump();
                Assert.False(menu.IsOpen);
                model.SelectGoalsCommand.Execute(null);
                Pump();
                model.GoalTasks.PersistTaskAsync = (change, insertAtTop) =>
                {
                    var command = GoalTaskPersistence.CreateSaveCommand(state, change, insertAtTop)!;
                    state = state with { Tasks = command.Tasks };
                    model.ApplyTaskState(state);
                    return Task.FromResult<LocalTaskDto?>(command.Tasks.Single(item => item.TaskId == change.TaskId));
                };
                Task<bool>? completion = null;
                Dispatcher.CurrentDispatcher.Invoke(() => completion = model.GoalTasks.CompleteTaskAsync(task));
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!completion!.IsCompleted && DateTime.UtcNow < deadline) { Thread.Sleep(20); Pump(); }
                Assert.True(completion.IsCompletedSuccessfully);
                Assert.True(completion.Result);
                Pump();
                Assert.DoesNotContain(task, model.GoalTasks.PendingTasks);
                Assert.Equal(1, model.GoalTasks.TodayCompletedCount);
            }
            catch (Exception exception) { failure = exception; }
            finally { if (menu is not null) menu.IsOpen = false; window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Next task presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Next task presentation failed.", failure);
    }

    private static void PressEnter(UIElement element) => element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element) yield return element;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void SavePreview(FrameworkElement page, string name, ContextMenu? menu = null, FrameworkElement? anchor = null)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_NEXT_TASK_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(249, 249, 250)), null, new Rect(0, 0, page.ActualWidth, page.ActualHeight));
            context.DrawRectangle(new VisualBrush(page), null, new Rect(0, 0, page.ActualWidth, page.ActualHeight));
            if (menu is not null && anchor is not null)
            {
                var point = anchor.TranslatePoint(new Point(anchor.ActualWidth, anchor.ActualHeight), page);
                context.DrawRectangle(new VisualBrush(menu), null, new Rect(point.X - menu.ActualWidth, point.Y - 2, menu.ActualWidth, menu.ActualHeight));
            }
        }
        var image = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
