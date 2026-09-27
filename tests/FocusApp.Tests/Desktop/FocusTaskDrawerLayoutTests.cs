using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
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
    public void DrawerRendersAllStatesAndEnterAddsSubTasksWithoutLosingFocus()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var session = new FocusSessionViewModel(runTimer: false);
                var target = new FocusTargetViewModel("专注目标");
                session.Start(30, target);
                session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                var vm = session.TaskDrawer;
                vm.ToggleCommand.Execute(null);
                vm.AddTaskCommand.Execute(null);
                vm.DraftTitle = "优化 Windows 数据布局";
                vm.Draft!.Description = "调整界面数据布局，避免元素遮挡。提升信息层级，让界面更清晰易用。";
                foreach (var title in new[] { "调整任务列表位置", "避免遮挡倒计时", "检查按钮间距与对齐" })
                { vm.SubTaskInput = title; vm.AddSubTaskCommand.Execute(null); }
                vm.CreateTaskCommand.Execute(null);
                target.Tasks[0].SubTasks[0].IsCompleted = true;
                vm.AddTaskCommand.Execute(null);
                vm.DraftTitle = "完善专注目标弹窗";
                vm.SubTaskInput = "检查模式切换";
                vm.AddSubTaskCommand.Execute(null);
                vm.CreateTaskCommand.Execute(null);

                vm.AddTaskCommand.Execute(null);
                vm.DraftTitle = "准备版本发布";
                vm.CreateTaskCommand.Execute(null);

                vm.AddTaskCommand.Execute(null);
                vm.DraftTitle = "只有备注的任务";
                vm.Draft!.Description = "保留备注，不显示空子任务区域。";
                vm.CreateTaskCommand.Execute(null);

                var layout = new Grid { Height = 710 };
                layout.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(session.FocusWindowWidth)) { Source = session });
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    layout.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });

                var focus = new FocusFlowView { DataContext = session, Width = 800, HorizontalAlignment = HorizontalAlignment.Left };
                var drawer = new FocusTaskDrawer { DataContext = vm, HorizontalAlignment = HorizontalAlignment.Right };
                layout.Children.Add(focus);
                layout.Children.Add(drawer);
                host = new Window { Content = layout, SizeToContent = SizeToContent.Width, Height = 710, WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
                host.Show();
                host.UpdateLayout();
                Assert.Equal(800, focus.ActualWidth);
                Assert.Equal(270, drawer.ActualWidth);
                Assert.Equal(710, drawer.ActualHeight);
                Assert.Equal(800, drawer.TransformToAncestor(layout).Transform(new Point()).X);
                var row = (Grid)Descendants(drawer).OfType<Border>().First(item => item.Name == "RowHover").Child;
                var number = Descendants(row).OfType<CheckBox>().Single();
                Assert.Equal("1", Assert.Single(Descendants(number).OfType<TextBlock>()).Text);
                var addButton = (Button)drawer.FindName("DrawerAddTaskButton");
                Assert.All(Descendants(addButton).OfType<TextBlock>(), label =>
                    Assert.Equal(Color.FromRgb(244, 119, 40), ((SolidColorBrush)label.Foreground).Color));
                Assert.DoesNotContain(Descendants(drawer).OfType<TextBlock>(), item => item.IsVisible && item.Text == "子任务 · 0/0");
                Assert.Equal(2, layout.Children.Count);
                Render("collapsed");
                var secondRow = Descendants(drawer).OfType<Border>().Single(item => item.Name == "RowHover" && item.DataContext == target.Tasks[1]);
                var secondRowBefore = secondRow.TransformToAncestor(drawer).Transform(new Point()).Y;
                var firstBody = Descendants(drawer).OfType<StackPanel>().Single(item => item.Name == "InlineTaskDetails" && item.DataContext == target.Tasks[0]);
                var progress = Descendants(row).OfType<TextBlock>().Single(item => item.Name == "CollapsedSubTaskProgress");
                Assert.True(progress.IsVisible);
                ((Border)row.Parent).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                Assert.True(target.Tasks[0].IsExpanded);
                host.UpdateLayout();
                Assert.True(firstBody.IsVisible);
                Assert.False(progress.IsVisible);
                Assert.Equal(1070, layout.ActualWidth);
                Assert.Equal(800, focus.ActualWidth);
                Assert.Equal(270, drawer.ActualWidth);
                Assert.Equal(800, drawer.TransformToAncestor(layout).Transform(new Point()).X);
                Assert.True(secondRow.TransformToAncestor(drawer).Transform(new Point()).Y > secondRowBefore);
                var arrow = Descendants(Descendants(row).OfType<Button>().Single()).OfType<System.Windows.Shapes.Path>().Single();
                Assert.Equal(90, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
                var completedChild = Descendants(firstBody).OfType<TextBlock>().Single(item => item.Text == "调整任务列表位置");
                Assert.Contains(completedChild.TextDecorations, decoration => decoration.Location == TextDecorationLocation.Strikethrough);
                Assert.Equal("调整任务列表位置", target.Tasks[0].SortedSubTasks.Last().Title);
                Render("expanded");
                vm.ToggleExpandedCommand.Execute(target.Tasks[0]);
                host.UpdateLayout();
                Assert.False(firstBody.IsVisible);
                Assert.True(progress.IsVisible);
                Assert.Equal(secondRowBefore, secondRow.TransformToAncestor(drawer).Transform(new Point()).Y);
                Assert.Equal(1070, layout.ActualWidth);
                var emptyRow = Descendants(drawer).OfType<Border>().Single(item => item.Name == "RowHover" && item.DataContext == target.Tasks[2]);
                var emptyHeight = emptyRow.Parent is FrameworkElement itemRow ? itemRow.ActualHeight : 0;
                vm.ToggleExpandedCommand.Execute(target.Tasks[2]);
                host.UpdateLayout();
                var emptyBody = Descendants(drawer).OfType<StackPanel>().Single(item => item.Name == "InlineTaskDetails" && item.DataContext == target.Tasks[2]);
                Assert.Equal(0, emptyBody.ActualHeight);
                Assert.Equal(emptyHeight, ((FrameworkElement)emptyRow.Parent).ActualHeight);
                Assert.DoesNotContain(Descendants(emptyBody).OfType<TextBlock>(), item => item.IsVisible);
                vm.ToggleExpandedCommand.Execute(target.Tasks[1]);
                host.UpdateLayout();
                var childOnlyBody = Descendants(drawer).OfType<StackPanel>().Single(item => item.Name == "InlineTaskDetails" && item.DataContext == target.Tasks[1]);
                Assert.DoesNotContain(Descendants(childOnlyBody).OfType<TextBlock>(), item => item.IsVisible && item.Text == "备注");
                vm.ToggleExpandedCommand.Execute(target.Tasks[3]);
                host.UpdateLayout();
                var noteOnlyBody = Descendants(drawer).OfType<StackPanel>().Single(item => item.Name == "InlineTaskDetails" && item.DataContext == target.Tasks[3]);
                Assert.Contains(Descendants(noteOnlyBody).OfType<TextBlock>(), item => item.IsVisible && item.Text == "备注");
                Assert.DoesNotContain(Descendants(noteOnlyBody).OfType<TextBlock>(), item => item.IsVisible && item.Text == "子任务 · 0/0");
                vm.ToggleExpandedCommand.Execute(target.Tasks[0]);
                host.UpdateLayout();
                var parentMenu = Descendants(drawer).OfType<Border>().First(item => item.ContextMenu is not null).ContextMenu!;
                parentMenu.PlacementTarget = Descendants(drawer).OfType<Border>().First(item => item.ContextMenu == parentMenu);
                parentMenu.IsOpen = true;
                host.UpdateLayout();
                Assert.Equal(new[] { "编辑任务名称", "编辑备注", "删除任务" }, parentMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header));
                RenderMenu(parentMenu, "task-menu");
                parentMenu.Items.OfType<MenuItem>().ElementAt(1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                parentMenu.IsOpen = false;
                host.UpdateLayout();
                Assert.True(vm.IsMultilineEdit);
                Assert.Single(Descendants(drawer).OfType<TextBox>().Where(item => item.Name == "InlineEditInput" && item.IsVisible));
                Render("editing-remark");
                vm.EditValue = "更新备注";
                vm.SaveEditCommand.Execute(null);
                Assert.Equal("更新备注", target.Tasks[0].Description);
                var childMenuHost = Descendants(drawer).OfType<Border>().First(item => item.IsVisible && item.DataContext is FocusSubTaskViewModel && item.ContextMenu is not null);
                var childMenu = childMenuHost.ContextMenu!;
                childMenu.PlacementTarget = childMenuHost;
                childMenu.IsOpen = true;
                host.UpdateLayout();
                Assert.Equal(new[] { "编辑子任务名称", "删除子任务" }, childMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header));
                RenderMenu(childMenu, "subtask-menu");
                childMenu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                childMenu.IsOpen = false;
                Assert.Equal("编辑子任务名称", vm.EditLabel);
                vm.CancelEditCommand.Execute(null);
                var expandButton = Descendants(row).OfType<Button>().Single();
                expandButton.Command.Execute(expandButton.CommandParameter);
                Assert.False(target.Tasks[0].IsExpanded);
                vm.ToggleExpandedCommand.Execute(target.Tasks[0]);

                vm.AddTaskCommand.Execute(null);
                vm.DraftTitle = "优化自动屏蔽规则";
                vm.Draft!.Description = "优化弹窗层级、按钮间距与规则说明展示方式。";
                Assert.False(vm.IsDetailsExpanded);
                Render("creating-collapsed");
                Assert.False(((TextBox)drawer.FindName("DraftTaskDescriptionInput")).IsVisible);
                vm.ToggleDetailsCommand.Execute(null);
                var input = (TextBox)drawer.FindName("DraftSubTaskInput");
                host.UpdateLayout();
                input.Focus();
                input.Text = "梳理规则说明文案";
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!, 0, Key.Enter)
                    { RoutedEvent = Keyboard.KeyDownEvent };
                input.RaiseEvent(args);
                Assert.Single(vm.Draft.SubTasks);
                Assert.Equal(string.Empty, input.Text);
                Assert.True(input.IsKeyboardFocused);
                input.Text = "调整弹窗层级与间距";
                input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                Assert.Equal(2, vm.Draft.SubTasks.Count);
                Render("creating-expanded");
                vm.ToggleDetailsCommand.Execute(null);
                Render("creating-collapsed");
                vm.ToggleDetailsCommand.Execute(null);
                for (var index = 2; index < 20; index++) { vm.SubTaskInput = $"子任务 {index + 1}"; vm.AddSubTaskCommand.Execute(null); }
                host.UpdateLayout();
                var sheet = (Border)drawer.FindName("CreationSheet");
                Assert.InRange(sheet.ActualHeight, 300, 520);
                var button = (Button)drawer.FindName("DrawerCreateTaskButton");
                var buttonBottom = button.TransformToAncestor(drawer).Transform(new Point(0, button.ActualHeight)).Y;
                Assert.InRange(buttonBottom, 600, 710);
                vm.CancelCreationCommand.Execute(null);
                Assert.Equal(4, target.Tasks.Count);
                target.Tasks[0].IsCompleted = true;
                host.UpdateLayout();
                Assert.Same(target.Tasks[0], vm.Tasks.Last());
                var completedTitle = Descendants(drawer).OfType<TextBlock>().Single(item => item.Name == "TaskTitle" && item.Text == target.Tasks[0].Name);
                Assert.Contains(completedTitle.TextDecorations, decoration => decoration.Location == TextDecorationLocation.Strikethrough);
                Render("completed");
                vm.CloseCommand.Execute(null);

                void RenderMenu(ContextMenu menu, string state)
                {
                    var directory = Environment.GetEnvironmentVariable("FOCUSAPP_TASK_DRAWER_QA_DIR");
                    if (string.IsNullOrEmpty(directory)) return;
                    menu.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(menu);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"task-drawer-{state}.png"));
                    encoder.Save(stream);
                }

                void Render(string state)
                {
                    ((TranslateTransform)((Border)drawer.FindName("CreationSheet")).RenderTransform).BeginAnimation(TranslateTransform.YProperty, null);
                    // Window resizing is dispatched to the HWND; flush it before capturing the visual.
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                    host.UpdateLayout();
                    layout.UpdateLayout();
                    Assert.True(layout.ActualWidth == host.ActualWidth, $"layout={layout.ActualWidth}; host Width={host.Width}, Actual={host.ActualWidth}, Max={host.MaxWidth}, workArea={SystemParameters.WorkArea.Width}");
                    var directory = Environment.GetEnvironmentVariable("FOCUSAPP_TASK_DRAWER_QA_DIR");
                    if (string.IsNullOrEmpty(directory)) return;
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap((int)layout.ActualWidth, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(layout);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(directory, $"task-drawer-{state}.png"));
                    encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { host?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        Assert.Null(failure);
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
