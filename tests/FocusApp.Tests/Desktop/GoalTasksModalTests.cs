using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalTasksModalTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ModalShowsCompletedHistoryWithoutCreationOrTabs()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "GoalTasksModal.xaml"));
        var card = document.Descendants(Presentation + "Border").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalTasksCard");
        Assert.Equal("450", (string?)card.Attribute("Width"));
        Assert.Equal("500", (string?)card.Attribute("Height"));
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "已完成任务");
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "{Binding CompletedCountSummary}");
        Assert.Contains(card.Descendants(Presentation + "TextBox"), element => ((string?)element.Attribute("Text"))?.Contains("CompletedSearchQuery", StringComparison.Ordinal) == true);
        Assert.Contains(card.Descendants(Presentation + "MenuItem"), element => (string?)element.Attribute("Command") == "{Binding SetCompletedSortCommand}");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("Command") == "{Binding EnterCompletedSelectionCommand}");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("Command") == "{Binding RestoreSelectedCompletedCommand}");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("Command") == "{Binding DeleteSelectedCompletedCommand}");
        var completed = Assert.Single(card.Descendants(Presentation + "ItemsControl").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTasksControl"));
        Assert.Equal("{Binding CompletedGroups}", (string?)completed.Attribute("ItemsSource"));
        var groupHeader = Assert.Single(completed.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTaskGroupHeader"));
        Assert.Contains(groupHeader.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding Title}");
        Assert.Contains(groupHeader.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding Subtitle}");
        var taskRow = Assert.Single(completed.Descendants(Presentation + "Border").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTaskRow"));
        Assert.Equal("51", (string?)taskRow.Attribute("MinHeight"));
        Assert.Equal("0,0,0,1", (string?)taskRow.Attribute("BorderThickness"));
        Assert.Contains(taskRow.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding Name}");
        Assert.Contains(taskRow.Descendants(Presentation + "Border"), element => (string?)element.Attribute(Xaml + "Name") == "CompletedIndicator");
        Assert.Contains(taskRow.Descendants(Presentation + "Border"), element => (string?)element.Attribute(Xaml + "Name") == "TaskCheckbox");
        Assert.Contains(taskRow.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute(Xaml + "Name") == "CompletedTaskRemark");
        Assert.Contains(taskRow.Descendants(Presentation + "ItemsControl"), element => (string?)element.Attribute(Xaml + "Name") == "CompletedTaskSubTasks");
        Assert.DoesNotContain(card.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "仅显示已完成的任务");
        Assert.DoesNotContain(card.Descendants(Presentation + "ItemsControl"), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding PendingTasks}");
    }

    [Fact]
    public void CompletedModalRendersDetailsAndSelectionAtFixedSizeAndClosesWithEscape()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var now = new DateTimeOffset(new DateTime(2026, 9, 18, 14, 32, 0));
                var model = new GoalTasksViewModel(() => now);
                var goal = new GoalOverviewItemViewModel("goal", "学习", "", "", false, false);
                var names = new[] { "完成登录页面", "修复自动屏蔽时间轴", "测试跨日数据", "优化移动端适配" };
                var data = names.Select((name, index) => new LocalTaskDto($"pending-{index}", goal.GoalId, name, false, index, now.AddDays(-8), now)).ToList();
                data.AddRange(names.Take(2).Select((name, index) => new LocalTaskDto($"completed-{index}", goal.GoalId, name, true, index + 4, now.AddDays(-8), now)
                {
                    CompletedAtUtc = now.AddMinutes(-index * 60),
                    Description = index == 0 ? "检查任务备注和子任务的层级展示。" : "",
                    SubTasks = index == 0 ? [new LocalSubTaskDto("child-0", "completed-0", "完善弹窗样式", true, 0, now.AddDays(-1), now)] : []
                }));
                data.AddRange(names.Skip(2).Select((name, index) => new LocalTaskDto($"yesterday-{index}", goal.GoalId, name, true, index + 6, now.AddDays(-8), now)
                    { CompletedAtUtc = now.AddDays(-1).AddMinutes(-index * 60) }));
                model.ApplyState(goal, data);
                model.OpenCompletedCommand.Execute(null);

                var modal = new GoalTasksModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 980, Height = 760, Content = modal, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000 };
                window.Show();
                Pump();

                var card = (Border)modal.FindName("GoalTasksCard");
                var completed = (ItemsControl)modal.FindName("CompletedTasksControl");
                Assert.Equal(450, card.ActualWidth);
                Assert.Equal(500, card.ActualHeight);
                Assert.Equal(2, completed.Items.Count);
                Assert.Equal(4, model.PendingCount);
                Assert.Equal(4, model.CompletedCount);
                Assert.Null(modal.FindName("NewTaskButton"));
                Assert.Null(modal.FindName("PendingTasksControl"));
                var row = VisualDescendants<Border>(completed).First(element => element.Name == "CompletedTaskRow");
                Assert.True(VisualDescendants<TextBlock>(row).Single(element => element.Name == "CompletedTaskRemark").IsVisible);
                Assert.True(VisualDescendants<ItemsControl>(row).Single(element => element.Name == "CompletedTaskSubTasks").IsVisible);
                var batchActions = (Grid)modal.FindName("CompletedBatchActions");
                Assert.False(batchActions.IsVisible);
                SavePreview(card, "completed-tasks-browse");

                var firstCompleted = model.CompletedGroups[0].Tasks[0];
                firstCompleted.IsMenuOpen = true;
                Pump();
                var more = VisualDescendants<Button>(row).Single(element => element.Name == "CompletedTaskMoreButton");
                Assert.True(more.IsVisible);
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, more));
                Pump();
                var menu = more.ContextMenu!;
                Assert.True(menu.IsOpen);
                var actions = menu.Items.Cast<MenuItem>().ToArray();
                Assert.Equal(new[] { "恢复任务", "删除任务" }, actions.Select(action => action.Header));
                Assert.All(actions, action => Assert.Same(firstCompleted, action.CommandParameter));
                SavePreview(card, "completed-tasks-menu", menu, more);
                menu.IsOpen = false;
                Pump();
                var sortButton = (Button)modal.FindName("CompletedSortButton");
                sortButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, sortButton));
                Pump();
                var sortMenu = sortButton.ContextMenu!;
                Assert.True(sortMenu.IsOpen);
                Assert.Equal(new[] { "最近完成", "最早完成", "任务名称", "完成时间" },
                    sortMenu.Items.Cast<MenuItem>().Select(item => item.Header));
                var nameSort = sortMenu.Items.Cast<MenuItem>().ElementAt(2);
                nameSort.Command.Execute(nameSort.CommandParameter);
                Assert.Equal("任务名称", model.CompletedSortLabel);
                model.SetCompletedSort("Recent");
                sortMenu.IsOpen = false;
                Pump();
                model.EnterCompletedSelectionMode();
                model.ToggleCompletedTaskSelection(firstCompleted);
                Pump();
                Assert.True(batchActions.IsVisible);
                Assert.False(VisualDescendants<TextBlock>(row).Single(element => element.Name == "CompletedTaskRemark").IsVisible);
                Assert.False(VisualDescendants<ItemsControl>(row).Single(element => element.Name == "CompletedTaskSubTasks").IsVisible);
                Assert.Equal(1, model.SelectedCompletedCount);
                SavePreview(card, "completed-tasks-selection");

                PressKey(modal, Key.Escape);
                Pump();
                Assert.False(model.IsSelectionMode);
                PressKey(modal, Key.Escape);
                Pump();
                Assert.False(model.IsOpen);
                Assert.Equal(Visibility.Collapsed, modal.Visibility);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Completed task dialog verification did not finish.");
        Assert.Null(failure);
    }

    [Fact]
    public void SelectionFooterAndSearchControlsArePresent()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "GoalTasksModal.xaml"));
        var card = document.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalTasksCard");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("AutomationProperties.Name") == "选择已完成任务");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("AutomationProperties.Name") == "恢复选中");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("AutomationProperties.Name") == "删除选中");
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding SelectedCompletedCount, StringFormat=已选择 {0} 项}");
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }
    private static void PressKey(UIElement element, Key key) =>
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });

    private static void SavePreview(FrameworkElement card, string name, ContextMenu? menu = null, FrameworkElement? anchor = null)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_GOAL_TASKS_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)Window.GetWindow(card)!.Content;
        var size = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(249, 249, 250)), null, size);
            drawing.DrawRectangle(new VisualBrush(root), null, size);
            if (menu is not null && anchor is not null)
            {
                var point = anchor.TranslatePoint(new Point(anchor.ActualWidth, anchor.ActualHeight), root);
                drawing.DrawRectangle(new VisualBrush(menu), null,
                    new Rect(point.X - menu.ActualWidth + 8, point.Y, menu.ActualWidth, menu.ActualHeight));
            }
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the FocusApp repository root.");
    }
}
