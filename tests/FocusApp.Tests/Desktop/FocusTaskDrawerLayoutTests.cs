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
    public void DrawerRendersNotesMenuSubtasksAndCompletedSectionWithinFixedSize()
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
                Render("expanded");

                var more = Descendants(card).OfType<Button>().Single(item => item.Name == "TaskMoreButton");
                Assert.Equal(Visibility.Collapsed, more.Visibility);
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
                vm.CancelCreationCommand.Execute(null);

                task.IsCompleted = true;
                host.UpdateLayout();
                Assert.Equal(new[] { next }, vm.Tasks);
                Assert.Equal(new[] { task }, vm.CompletedTasks);
                Assert.Equal("1/2", vm.TaskProgress);
                var completedList = (ItemsControl)drawer.FindName("DrawerCompletedTaskList");
                Assert.False(completedList.IsVisible);
                vm.ToggleCompletedCommand.Execute(null);
                host.UpdateLayout();
                Assert.True(completedList.IsVisible);
                Render("completed");
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
        Assert.DoesNotContain(document.Descendants(), item => (string?)item.Attribute(xaml + "Name") is "TaskCard" or "InlineEditor" or "CreationSheet");
        Assert.Single(document.Descendants(presentation + "Border").Where(item => (string?)item.Attribute(xaml + "Name") == "TaskEditorPanel"));
        Assert.DoesNotContain(document.Descendants(presentation + "MenuItem"), item => (string?)item.Attribute("Header") is "编辑任务名称" or "编辑备注");
        Assert.Equal(new[] { "编辑任务", "删除任务" }, document.Descendants(presentation + "MenuItem").Select(item => (string?)item.Attribute("Header")));
        var more = Assert.Single(document.Descendants(presentation + "Button").Where(item =>
            (string?)item.Attribute(xaml + "Name") == "TaskMoreButton"));
        Assert.Contains(more.Descendants(presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility" && (string?)setter.Attribute("Value") == "Collapsed");
        Assert.Contains(document.Descendants(presentation + "Trigger"), trigger =>
            (string?)trigger.Attribute("SourceName") == "TaskRowSurface" &&
            (string?)trigger.Attribute("Property") == "IsMouseOver" &&
            trigger.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("TargetName") == "TaskMoreButton" &&
                (string?)setter.Attribute("Property") == "Visibility" &&
                (string?)setter.Attribute("Value") == "Visible"));
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
