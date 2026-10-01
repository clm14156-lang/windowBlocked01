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
    public void ParentNumbersFollowPendingOrderAndFloatingCreationKeepsTasksClear()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var now = DateTimeOffset.Now;
                var parent = GoalNextTaskDetailsTests.TaskData("测试0554545") with
                {
                    SubTasks = [new LocalSubTaskDto("child", "测试0554545", "45554454545", false, 0, now, now)]
                };
                var second = GoalNextTaskDetailsTests.TaskData("测试03", 1);
                var third = GoalNextTaskDetailsTests.TaskData("测试024554", 2);
                var completed = GoalNextTaskDetailsTests.TaskData("已完成", 3) with { IsCompleted = true, CompletedAtUtc = now };
                var state = GoalNextTaskDetailsTests.State(parent, second, third, completed);
                state = state with { Targets = [state.Targets[0] with { Name = "3213", TargetDurationMinutes = null }] };
                var model = new StatisticsOverviewViewModel(false);
                model.ApplyState(state);
                model.SetUserAccess(true, true);
                model.SelectGoalsCommand.Execute(null);
                model.GoalTasks.PersistPendingTaskOrderAsync = (_, _) => Task.FromResult(true);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 1000, Height = 830, Content = page, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var items = (ItemsControl)page.FindName("GoalNextTasks");
                void AssertNumbers(params string[] names)
                {
                    var rows = Descendants<Grid>(items).Where(row => row.Name == "GoalNextTaskRow").ToArray();
                    Assert.Equal(names, rows.Select(row => ((FocusTaskViewModel)row.DataContext).Name));
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var check = Descendants<Button>(rows[index]).Single(button => button.Name == "GoalNextTaskCheckButton");
                        Assert.Equal((index + 1).ToString(), check.Content);
                        Assert.Same(model.GoalTasks.CompleteTaskCommand, check.Command);
                        Assert.Same(rows[index].DataContext, check.CommandParameter);
                    }
                }
                AssertNumbers(parent.Name, second.Name, third.Name);
                var childCheck = Descendants<Button>(items).Single(button => button.Name == "GoalSubTaskCheckButton");
                Assert.Null(childCheck.Content);
                var childSurface = (Border)childCheck.Template.FindName("CheckSurface", childCheck);
                Assert.Equal(childCheck.ActualHeight / 2, childSurface.CornerRadius.TopLeft);
                var firstParent = model.GoalTasks.PendingTasks[0];
                var moved = model.GoalTasks.MovePendingTaskAsync(model.GoalTasks.PendingTasks[2], firstParent, false);
                Assert.True(moved.GetAwaiter().GetResult());
                Pump();
                AssertNumbers(third.Name, parent.Name, second.Name);
                SavePreview(page, "goal-detail-numbered-reordered");

                model.ApplyTaskState(state);
                Pump();
                AssertNumbers(parent.Name, second.Name, third.Name);
                var weekly = (Border)page.FindName("GoalWeeklyInvestmentCard");
                var total = (Border)page.FindName("GoalTotalInvestmentCard");
                Assert.InRange(Math.Abs(weekly.ActualWidth - total.ActualWidth), 0, 1); // DPI rounding of equal star columns.
                Assert.Equal(weekly.ActualHeight, total.ActualHeight);
                Assert.All(new[] { weekly, total }, card => Assert.All(Descendants<Image>(card).Where(image => image.Source.ToString()!.Contains("mubiao_")), image =>
                {
                    Assert.Equal(Stretch.Uniform, image.Stretch);
                    Assert.InRange(image.Opacity, 0.1, 0.4);
                    Assert.False(image.IsHitTestVisible);
                }));
                var current = (Grid)page.FindName("CurrentGoalDetails");
                var sectionTitles = Descendants<TextBlock>(current).Where(text => text.Text is "投入概括" or "待办任务").ToArray();
                Assert.Equal(2, sectionTitles.Length);
                Assert.Same(sectionTitles[0].Style, sectionTitles[1].Style);
                var create = (Button)page.FindName("CreateNextTaskButton");
                var scroll = (ScrollViewer)page.FindName("GoalNextTasksScroll");
                var region = (Grid)page.FindName("GoalNextTasksRegion");
                var overlay = Assert.IsType<Canvas>(create.Parent);
                Assert.Equal(0, overlay.DesiredSize.Height);
                Assert.Equal(2, region.RowDefinitions.Count);
                Assert.InRange(Math.Abs(scroll.TranslatePoint(new Point(0, scroll.ActualHeight), region).Y - region.ActualHeight), 0, 1);
                var fullHeight = scroll.ActualHeight;
                create.Visibility = Visibility.Collapsed;
                Pump();
                Assert.Equal(fullHeight, scroll.ActualHeight);
                create.Visibility = Visibility.Visible;
                Pump();
                SavePreview(page, "goal-detail-numbered");
                create.Command.Execute(null);
                Pump();
                Assert.True(model.GoalTasks.IsCreating);
                Assert.True(((TextBox)page.FindName("GoalNewTaskNameTextBox")).IsVisible);
                model.GoalTasks.CancelCreation();
                model.ApplyTaskState(state with { Tasks = [parent, third, completed] });
                Pump();
                AssertNumbers(parent.Name, third.Name);
                model.ApplyTaskState(state with { Tasks = [parent with { IsCompleted = true, CompletedAtUtc = now }, third, completed] });
                Pump();
                AssertNumbers(third.Name);
                model.ApplyTaskState(state);
                Pump();
                AssertNumbers(parent.Name, second.Name, third.Name);

                // Overflow and narrow cards reproduce the two reported clipping cases.
                model.ApplyTaskState(state with { Tasks = Enumerable.Range(0, 18)
                    .Select(index => GoalNextTaskDetailsTests.TaskData($"任务 {index + 1}", index)).ToArray() });
                model.SelectedGoal!.UpdateDetails(null, 100 * 60);
                var end = DateTime.Today.AddHours(12);
                model.FocusSessionRecords.Add(new FocusSessionRecordViewModel(end.AddMinutes(-106), end,
                    model.SelectedGoal.GoalId, model.SelectedGoal.Name, "", 0));
                var mainData = (TextBlock)page.FindName("GoalTotalInvestmentText");
                var progress = (Grid)page.FindName("GoalInvestmentProgress");
                var safeArea = (Border)page.FindName("GoalTaskBottomSafeArea");
                foreach (var testWidth in new[] { 660, 760, 1000 })
                {
                    window.Width = testWidth;
                    window.Height = 710;
                    Pump();
                    Assert.Equal("1 小时 46 分钟 / 100小时", new System.Windows.Documents.TextRange(mainData.ContentStart, mainData.ContentEnd).Text);
                    Assert.Equal(TextTrimming.None, mainData.TextTrimming);
                    var dataBounds = mainData.TransformToAncestor(total).TransformBounds(new Rect(mainData.RenderSize));
                    var progressBounds = progress.TransformToAncestor(total).TransformBounds(new Rect(progress.RenderSize));
                    Assert.InRange(dataBounds.Left, 11, total.ActualWidth);
                    Assert.True(dataBounds.Right <= total.ActualWidth - 11, "The whole metric must fit inside the card's right padding.");
                    Assert.True(dataBounds.Bottom <= progressBounds.Top + 1, "The progress row must stay below the metric.");
                    Assert.InRange(Math.Abs(scroll.TranslatePoint(new Point(0, scroll.ActualHeight), region).Y - region.ActualHeight), 0, 1);
                    Assert.True(scroll.ScrollableHeight > 0);
                    Assert.True(safeArea.ActualHeight >= create.ActualHeight + Canvas.GetBottom(create));
                    scroll.ScrollToEnd();
                    Pump();
                    var lastRow = Descendants<Grid>(items).Last(row => row.Name == "GoalNextTaskRow");
                    var lastBounds = lastRow.TransformToAncestor(region).TransformBounds(new Rect(lastRow.RenderSize));
                    var buttonBounds = create.TransformToAncestor(region).TransformBounds(new Rect(create.RenderSize));
                    Assert.True(lastBounds.Bottom <= buttonBounds.Top - 7, "The final task must scroll clear of the floating button.");
                    Assert.InRange(region.ActualWidth - buttonBounds.Right, 11, 13);
                    Assert.InRange(region.ActualHeight - buttonBounds.Bottom, 7, 9);
                    SavePreview(page, $"goal-detail-overlay-bottom-{testWidth}");
                    scroll.ScrollToHome();
                    Pump();
                    SavePreview(page, $"goal-detail-overlay-{testWidth}");
                }

                model.SelectedGoal.UpdateDetails(null, 9999 * 60 + 59);
                window.Width = 660;
                Pump();
                var longestBounds = mainData.TransformToAncestor(total).TransformBounds(new Rect(mainData.RenderSize));
                Assert.EndsWith("9999小时 59分钟", new System.Windows.Documents.TextRange(mainData.ContentStart, mainData.ContentEnd).Text);
                Assert.True(longestBounds.Right <= total.ActualWidth - 11);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Goal detail UI verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Goal detail UI verification failed.", failure);
    }

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
                var plainRows = Descendants<Grid>(page).Where(element => element.Name == "GoalNextTaskRow" &&
                    element.DataContext is FocusTaskViewModel { HasSubTasks: false }).ToArray();
                Assert.Equal(3, plainRows.Length);
                Assert.All(plainRows, plainRow =>
                {
                    Assert.False(Descendants<Button>(plainRow).Single(button => button.Name == "GoalAddSubTaskButton").IsVisible);
                    Assert.False(Descendants<Button>(plainRow).Single(button => button.Name == "GoalTaskSubTasksToggle").IsVisible);
                    Assert.False(Descendants<ItemsControl>(plainRow).Single(control => control.Name == "GoalTaskSubTasks").IsVisible);
                });
                var childRow = Descendants<Grid>(children).First(element => element.Name == "GoalSubTaskRow");
                var childMore = Descendants<Button>(childRow).Single(button => button.Name == "GoalSubTaskMoreButton");
                Assert.Equal(Visibility.Hidden, childMore.Visibility);
                var hoverTrigger = Assert.Single(childMore.Style.Triggers.OfType<DataTrigger>().Where(trigger =>
                    trigger.Binding is System.Windows.Data.Binding { ElementName: "GoalSubTaskRow" }));
                var hoverBinding = Assert.IsType<System.Windows.Data.Binding>(hoverTrigger.Binding);
                Assert.Equal("GoalSubTaskRow", hoverBinding.ElementName);
                Assert.Equal("IsMouseOver", hoverBinding.Path.Path);
                var childText = Descendants<TextBlock>(childRow).Single(text => text.Text == "任务07");
                var childCheckButton = Descendants<Button>(childRow).Single(button => button.Name == "GoalSubTaskCheckButton");
                var textPosition = childText.TranslatePoint(new Point(), page);
                var checkPosition = childCheckButton.TranslatePoint(new Point(), page);
                childMore.Visibility = Visibility.Visible;
                Pump();
                Assert.Equal(textPosition, childText.TranslatePoint(new Point(), page));
                Assert.Equal(checkPosition, childCheckButton.TranslatePoint(new Point(), page));
                Assert.Equal(more.ActualWidth, childMore.ActualWidth);
                Assert.InRange(Math.Abs(more.TranslatePoint(new Point(more.ActualWidth, 0), page).X -
                    childMore.TranslatePoint(new Point(childMore.ActualWidth, 0), page).X), 0, 1); // DPI layout rounding.
                SavePreview(page, "subtask-hover");
                childMore.ClearValue(UIElement.VisibilityProperty);
                childMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, childMore));
                Pump();
                menu = childMore.ContextMenu;
                Assert.True(menu.IsOpen);
                var childActions = menu.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(new[] { "编辑子任务", "删除子任务" }, childActions.Select(action => action.Header));
                Assert.Single(menu.Items.OfType<Separator>());
                Assert.All(childActions, action => Assert.Same(task.SubTasks[0], action.CommandParameter));
                var deleteIcon = (System.Windows.Shapes.Path)childActions[1].Template.FindName("MenuIcon", childActions[1]);
                Assert.Equal(Color.FromRgb(230, 92, 92), ((SolidColorBrush)deleteIcon.Stroke).Color);
                SavePreview(page, "subtask-menu", menu, childMore);
                childActions[0].Command.Execute(childActions[0].CommandParameter);
                menu.IsOpen = false;
                Pump();
                var childEditor = Descendants<TextBox>(children).Single(editor => editor.Name == "GoalExistingSubTaskEditor" && editor.IsVisible);
                Assert.Equal("任务07", childEditor.Text);
                childEditor.Text = "编辑后的子任务";
                Pump();
                SavePreview(page, "subtask-title-editor");
                PressEnter(childEditor);
                Pump();
                Assert.Equal("编辑后的子任务", task.SubTasks[0].Title);
                Assert.False(task.SubTasks[0].NextTaskEditor.IsActive);
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
                var childCheck = Descendants<Button>(children).Last(button => button.Name == "GoalSubTaskCheckButton");
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
                Assert.False(addSubTask.IsVisible);
                Assert.True(Descendants<TextBlock>(row).Single(element => element.Name == "GoalTaskRemark").IsVisible);
                capsule.Command.Execute(capsule.CommandParameter);
                Pump();
                foreach (var child in task.SubTasks.ToArray()) model.GoalTasks.DeleteSubTaskCommand.Execute(child);
                Pump();
                Assert.Empty(task.SubTasks);
                Assert.False(children.IsVisible);
                Assert.False(capsule.IsVisible);
                Assert.False(addSubTask.IsVisible);
                Assert.True(more.IsVisible);
                SavePreview(page, "subtask-last-deleted");
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
