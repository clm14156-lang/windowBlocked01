using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusTaskDrawerLayoutTests
{
    [Fact]
    public void MainWindowHostsTheDrawerOutsideTheUnchangedFocusArea()
    {
        var root = FindRepositoryRoot();
        var main = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        var drawer = Assert.Single(main.Descendants().Where(item => item.Name.LocalName == "FocusTaskDrawer"));
        Assert.Equal("Right", (string?)drawer.Attribute("HorizontalAlignment"));
        Assert.Equal("{Binding HomePage.FocusSession.TaskDrawer}", (string?)drawer.Attribute("DataContext"));
        Assert.DoesNotContain(main.Descendants(), item => item.Name.LocalName == "FocusTaskDetailsPanel");
        Assert.Equal("1070", (string?)main.Root!.Attribute("MaxWidth"));
        var focus = XDocument.Load(Path.Combine(root, "src", "FocusApp.Desktop", "Views", "FocusFlowView.xaml"));
        Assert.DoesNotContain(focus.Descendants(), item => item.Name.LocalName is "FocusTaskWindow" or "FocusTaskDrawer");
    }

    [Fact]
    public void DrawerRendersNotesMenuSubtasksAndCompletedItemsWithinFixedSize()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var now = DateTimeOffset.UtcNow;
                var target = new FocusTargetViewModel("专注目标");
                var task = target.AddTask("优化 Windows 数据布局");
                task.Description = "调整界面数据布局，避免元素遮挡。提升信息层级，让界面更清晰易用。";
                task.SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto("sub-1", task.TaskId, "调整任务列表位置", false, 0, now, now)));
                task.SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto("sub-2", task.TaskId, "检查按钮间距与对齐", false, 1, now, now)));
                var next = target.AddTask("新任务");
                var session = new FocusSessionViewModel(runTimer: false);
                session.Start(30, target);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                var vm = session.TaskDrawer;
                vm.ToggleCommand.Execute(null);

                var layout = new Grid { Height = 710 };
                layout.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(session.FocusWindowWidth)) { Source = session });
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    layout.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                var focus = new FocusFlowView { DataContext = session, Width = 800, HorizontalAlignment = HorizontalAlignment.Left };
                var drawer = new FocusTaskDrawer { DataContext = vm, HorizontalAlignment = HorizontalAlignment.Right };
                layout.Children.Add(focus);
                layout.Children.Add(drawer);
                host = new Window { Content = layout, SizeToContent = SizeToContent.Width, Height = 710,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
                host.Show();
                host.UpdateLayout();

                void Render(string state)
                {
                    var directory = Environment.GetEnvironmentVariable("FOCUSAPP_TASK_DRAWER_QA_DIR");
                    if (string.IsNullOrEmpty(directory)) return;
                    Directory.CreateDirectory(directory);
                    host.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)layout.ActualWidth, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(layout);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"task-drawer-{state}.png"));
                    encoder.Save(stream);
                }

                Assert.Equal(800, focus.ActualWidth);
                Assert.Equal(270, drawer.ActualWidth);
                Assert.Equal(710, drawer.ActualHeight);
                Assert.Equal(1070, layout.ActualWidth);
                var card = Descendants(drawer).OfType<Grid>().Single(item => item.Name == "TaskRowSurface" && item.DataContext == task);
                var mainCheck = Descendants(card).OfType<CheckBox>().First();
                var chrome = (Border)mainCheck.Template.FindName("CheckChrome", mainCheck);
                var number = (TextBlock)mainCheck.Template.FindName("TaskNumber", mainCheck);
                Assert.Equal(new CornerRadius(3), chrome.CornerRadius);
                Assert.Equal("1", number.Text);
                Assert.Equal(11, number.FontSize);
                Assert.Equal(HorizontalAlignment.Center, number.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, number.VerticalAlignment);
                var note = Descendants(card).OfType<TextBlock>().Single(item => item.Name == "InlineDescription");
                Assert.True(note.IsVisible);
                Assert.Equal(task.Description, note.Text);
                Assert.Equal(36, note.MaxHeight);
                var toggle = Descendants(card).OfType<Button>().Single(item => item.Name == "SubTaskToggle");
                Assert.True(toggle.IsVisible);
                Assert.Equal("子任务 0/2", Descendants(toggle).OfType<TextBlock>().Single().Text);
                var childList = Descendants(card).OfType<ItemsControl>().Single(item => item.Name == "InlineSubTaskList");
                Assert.False(childList.IsVisible);
                Render("collapsed");

                toggle.Command.Execute(toggle.CommandParameter);
                host.UpdateLayout();
                Assert.True(childList.IsVisible);
                Assert.Equal("收起", Descendants(toggle).OfType<TextBlock>().Single().Text);
                Assert.Equal(2, childList.Items.Count);
                Assert.Equal(new Thickness(0), ((Border)childList.Parent).BorderThickness);
                Assert.True(CanDrag(note, card));
                Assert.False(CanDrag(mainCheck, card));
                Assert.False(CanDrag(Descendants(toggle).OfType<TextBlock>().Single(), card));
                Assert.False(CanDrag(Descendants(childList).OfType<TextBlock>().First(), card));
                Render("expanded");

                var more = Descendants(card).OfType<Button>().Single(item => item.Name == "TaskMoreButton");
                Assert.False(CanDrag(more, card));
                Assert.Equal(Visibility.Visible, more.Visibility);
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = more.ContextMenu!;
                host.UpdateLayout();
                Assert.True(menu.IsOpen);
                Assert.True(more.IsVisible);
                Assert.Same(more, menu.PlacementTarget);
                Assert.Equal(new[] { "编辑任务", "删除任务" },
                    menu.Items.OfType<MenuItem>().Select(item => (string)item.Header));
                menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.IsOpen = false;
                Assert.True(vm.IsEditingTask);
                vm.Draft!.Description = "更新备注";
                Render("editing");
                vm.CreateTaskCommand.Execute(null);
                Assert.Equal("更新备注", note.Text);

                vm.AddTaskCommand.Execute(null);
                Render("creating");
                host.UpdateLayout();
                var scrim = (Border)drawer.FindName("TaskEditorScrim");
                var editor = (Border)drawer.FindName("TaskEditorPanel");
                Assert.True(scrim.IsVisible);
                Assert.InRange(drawer.ActualWidth - scrim.ActualWidth, 0, 2);
                Assert.Equal(4, Grid.GetRowSpan(scrim));
                Assert.Equal(1, Panel.GetZIndex(scrim));
                Assert.Equal(2, Panel.GetZIndex((UIElement)editor.Parent));
                Assert.False(((FrameworkElement)drawer.FindName("DraftDescriptionArea")).IsVisible);
                Assert.False(((FrameworkElement)drawer.FindName("DraftSubTaskArea")).IsVisible);
                Assert.False(((FrameworkElement)drawer.FindName("DraftAddDescriptionButton")).IsVisible);
                Assert.False(((FrameworkElement)drawer.FindName("DraftAddSubTaskButton")).IsVisible);
                Assert.False(((Button)drawer.FindName("DrawerCreateTaskButton")).IsEnabled);
                var initialHeight = editor.ActualHeight;
                var list = (ScrollViewer)drawer.FindName("TaskDrawerScrollViewer");
                var underlyingListHeight = list.ActualHeight;
                vm.DraftTitle = "完成首页交互优化";
                host.UpdateLayout();
                Assert.True(((FrameworkElement)drawer.FindName("DraftAddDescriptionButton")).IsVisible);
                Assert.True(((FrameworkElement)drawer.FindName("DraftAddSubTaskButton")).IsVisible);
                Assert.True(((Button)drawer.FindName("DrawerCreateTaskButton")).IsEnabled);
                vm.ExpandDescriptionCommand.Execute(null);
                vm.DraftDescription = "多行描述\n补充交互细节";
                vm.BeginSubTaskCommand.Execute(null);
                for (var i = 0; i < 20; i++) { vm.SubTaskInput = $"子任务 {i + 1}"; vm.AddSubTaskCommand.Execute(null); }
                host.UpdateLayout();
                Assert.True(((FrameworkElement)drawer.FindName("DraftDescriptionArea")).IsVisible);
                Assert.True(((FrameworkElement)drawer.FindName("DraftSubTaskArea")).IsVisible);
                Assert.True(editor.ActualHeight > initialHeight);
                Assert.InRange(editor.ActualHeight, initialHeight, 500);
                var contentScroll = (ScrollViewer)drawer.FindName("DraftContentScroll");
                Assert.True(contentScroll.ScrollableHeight > 0);
                Assert.Equal(underlyingListHeight, list.ActualHeight);
                var footer = (Button)drawer.FindName("DrawerCreateTaskButton");
                Assert.True(footer.TranslatePoint(new Point(), editor).Y + footer.ActualHeight <= editor.ActualHeight);
                Render("progressive-full");
                editor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });
                Assert.True(vm.IsCreating);
                scrim.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent
                });
                Assert.False(vm.IsCreating);
                Assert.False(scrim.IsVisible);

                task.IsCompleted = true;
                host.UpdateLayout();
                Assert.Equal(new[] { next }, vm.Tasks);
                Assert.Equal(new[] { task }, vm.TodayCompletedTasks);
                Assert.Equal("1/2", vm.TaskProgress);
                var visibleList = (ItemsControl)drawer.FindName("DrawerTaskList");
                Assert.Equal(new[] { next, task }, visibleList.Items.Cast<FocusTaskViewModel>());
                Assert.Null(drawer.FindName("DrawerTodayCompletedSection"));
                var completedCard = Descendants(visibleList).OfType<Grid>().Single(item => item.Name == "TaskRowSurface" && item.DataContext == task);
                Assert.Equal(Visibility.Collapsed, number.Visibility);
                Assert.Equal(Visibility.Visible, ((UIElement)mainCheck.Template.FindName("Tick", mainCheck)).Visibility);
                Assert.True(Descendants(completedCard).OfType<TextBlock>().Single(item => item.Name == "InlineDescription").IsVisible);
                Assert.True(Descendants(completedCard).OfType<Button>().Single(item => item.Name == "SubTaskToggle").IsVisible);
                Render("completed");

                // Exercise the same pointer lifecycle used by the routed mouse handlers.
                // Coordinates are injected so the test doesn't move the user's mouse.
                object? DragCall(string method, params object[] arguments) => typeof(FocusTaskDrawer)
                    .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(drawer, arguments);
                var origin = completedCard.TranslatePoint(new Point(70, 20), list);
                DragCall("PrepareTaskDrag", completedCard, completedCard, origin);
                DragCall("UpdateTaskDrag", origin, MouseButtonState.Pressed);
                Assert.Equal(0.72, completedCard.Opacity);
                Assert.Equal(false, DragCall("CompleteTaskDrag", new Point(70, 2)));
                Assert.Equal(new[] { next, task }, vm.VisibleTasks);

                vm.ToggleTaskCompletedCommand.Execute(task);
                vm.ToggleExpandedCommand.Execute(task);
                host.UpdateLayout();
                completedCard = Descendants(visibleList).OfType<Grid>().Single(item => item.Name == "TaskRowSurface" && item.DataContext == task);
                origin = completedCard.TranslatePoint(new Point(70, 20), list);
                var nextCard = Descendants(visibleList).OfType<Grid>().Single(item => item.Name == "TaskRowSurface" && item.DataContext == next);
                var drop = nextCard.TranslatePoint(new Point(70, 2), list);
                DragCall("PrepareTaskDrag", completedCard, completedCard, origin);
                DragCall("UpdateTaskDrag", origin, MouseButtonState.Pressed);
                Assert.Equal(1, completedCard.Opacity);
                // Off-screen windows cannot capture the physical mouse; enter the
                // post-capture feedback stage without changing the user's input.
                DragCall("StartTaskDragFeedback");
                DragCall("UpdateTaskDrag", drop, MouseButtonState.Pressed);
                Assert.Equal(0.45, completedCard.Opacity);
                var insertionLine = (Border)drawer.FindName("TaskInsertionLine");
                Assert.Equal(Visibility.Visible, insertionLine.Visibility);
                Assert.Equal(new[] { next, task }, vm.VisibleTasks);
                Render("dragging");
                Assert.Equal(true, DragCall("CompleteTaskDrag", drop));
                host.UpdateLayout();
                Assert.False(drawer.IsMouseCaptured);
                Assert.Equal(Visibility.Collapsed, insertionLine.Visibility);
                Assert.Equal(new[] { task, next }, vm.VisibleTasks);
                Assert.Equal(new[] { 1, 2 }, vm.VisibleTasks.Select(item => item.DrawerNumber));
                Assert.True(task.IsExpanded);
                Assert.Equal("收起", task.DrawerSubTaskToggleLabel);
                Assert.Equal(2, task.SubTasks.Count);
                Assert.Equal(1, completedCard.Opacity);
                Render("reordered");

                completedCard = Descendants(visibleList).OfType<Grid>().Single(item => item.Name == "TaskRowSurface" && item.DataContext == task);
                nextCard = Descendants(visibleList).OfType<Grid>().Single(item => item.Name == "TaskRowSurface" && item.DataContext == next);
                var cancelPoint = nextCard.TranslatePoint(new Point(70, nextCard.ActualHeight - 2), list);
                origin = completedCard.TranslatePoint(new Point(70, 20), list);
                DragCall("PrepareTaskDrag", completedCard, completedCard, origin);
                DragCall("StartTaskDragFeedback");
                DragCall("UpdateTaskDrag", cancelPoint, MouseButtonState.Pressed);
                Assert.Equal(Visibility.Visible, insertionLine.Visibility);
                drawer.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                    { RoutedEvent = Mouse.LostMouseCaptureEvent, Source = drawer });
                Assert.Equal(Visibility.Collapsed, insertionLine.Visibility);
                Assert.Equal(new[] { task, next }, vm.VisibleTasks);
                DragCall("PrepareTaskDrag", completedCard, completedCard, origin);
                DragCall("StartTaskDragFeedback");
                DragCall("UpdateTaskDrag", cancelPoint, MouseButtonState.Pressed);
                Assert.Equal(true, DragCall("CompleteTaskDrag", new Point(-20, 2)));
                Assert.Equal(new[] { task, next }, vm.VisibleTasks);
                Assert.Equal(1, completedCard.Opacity);

                DragCall("PrepareTaskDrag", completedCard, completedCard, origin);
                DragCall("StartTaskDragFeedback");
                task.IsCompleted = true; // Completion from another UI aborts an in-flight drag.
                DragCall("UpdateTaskDrag", cancelPoint, MouseButtonState.Pressed);
                Assert.Equal(false, DragCall("CompleteTaskDrag", cancelPoint));
                Assert.Equal(new[] { next, task }, vm.VisibleTasks);
                Assert.Equal(Visibility.Collapsed, insertionLine.Visibility);
            }
            catch (Exception exception) { failure = exception; }
            finally { host?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Task drawer rendering timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void FlatTaskRowsAndSharedEditorKeepTheDrawerAtItsOriginalSize()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "FocusTaskDrawer.xaml"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        Assert.Equal("270", (string?)document.Root!.Attribute("Width"));
        Assert.Equal("710", (string?)document.Root.Attribute("Height"));
        Assert.DoesNotContain(document.Descendants(), item => (string?)item.Attribute(xaml + "Name") == "CompletedTasksToggle");
        Assert.DoesNotContain(document.Descendants(), item => (string?)item.Attribute(xaml + "Name") is "DrawerTodayCompletedTaskList" or "DrawerTodayCompletedSection");
        Assert.DoesNotContain(document.Descendants(presentation + "TextBlock"), item => (string?)item.Attribute("Text") == "今日完成");
        Assert.DoesNotContain(document.Descendants(), item => (string?)item.Attribute(xaml + "Name") is "TaskCard" or "InlineEditor" or "CreationSheet");
        Assert.Single(document.Descendants(presentation + "Border").Where(item => (string?)item.Attribute(xaml + "Name") == "TaskEditorPanel"));
        var scrim = Assert.Single(document.Descendants(presentation + "Border").Where(item => (string?)item.Attribute(xaml + "Name") == "TaskEditorScrim"));
        Assert.Equal("4", (string?)scrim.Attribute("Grid.RowSpan"));
        Assert.Equal("TaskEditorScrim_MouseLeftButtonDown", (string?)scrim.Attribute("MouseLeftButtonDown"));
        Assert.DoesNotContain(document.Descendants(presentation + "Border"), item => (string?)item.Attribute("Background") == "#FF791D");
        Assert.DoesNotContain(document.Descendants(presentation + "MenuItem"), item => (string?)item.Attribute("Header") is "编辑任务名称" or "编辑备注");
        Assert.Equal(new[] { "编辑任务", "删除任务" }, document.Descendants(presentation + "MenuItem").Select(item => (string?)item.Attribute("Header")));
        var more = Assert.Single(document.Descendants(presentation + "Button").Where(item =>
            (string?)item.Attribute(xaml + "Name") == "TaskMoreButton"));
        Assert.Contains(more.Descendants(presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Opacity" && (string?)setter.Attribute("Value") == "0.6");
        Assert.Contains(document.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("SourceName") == "TaskRowSurface" &&
            (string?)trigger.Attribute("Property") == "IsMouseOver" &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("TargetName") == "TaskMoreButton" &&
                (string?)setter.Attribute("Property") == "Opacity" &&
                (string?)setter.Attribute("Value") == "1"));
    }
    private static bool CanDrag(DependencyObject source, FrameworkElement row) =>
        (bool)typeof(FocusTaskDrawer).GetMethod("IsTaskDragSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [source, row])!;

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(29, 0)]
    [InlineData(31, 1)]
    [InlineData(179, 1)]
    [InlineData(181, 2)]
    [InlineData(340, 3)]
    public void DropPositionsUseWholeBlockHeightsIncludingExpandedSubtasks(double pointer, int expected)
    {
        var insertion = (int)typeof(FocusTaskDrawer).GetMethod("FindInsertionIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [pointer, new double[] { 0, 60, 300 }, new double[] { 60, 240, 60 }])!;
        Assert.Equal(expected, insertion);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
}
