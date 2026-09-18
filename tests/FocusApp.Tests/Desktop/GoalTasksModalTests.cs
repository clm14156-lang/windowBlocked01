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
    public void ModalIsCompletedOnlyWithFixedSizeAndNoCreationOrTabs()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "GoalTasksModal.xaml"));
        var card = document.Descendants(Presentation + "Border").Single(element =>
            (string?)element.Attribute(Xaml + "Name") == "GoalTasksCard");
        Assert.Equal("330", (string?)card.Attribute("Width"));
        Assert.Equal("400", (string?)card.Attribute("Height"));
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "已完成任务");
        Assert.DoesNotContain(card.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") is "全部任务" or "未完成");
        Assert.DoesNotContain(card.Descendants(Presentation + "Button"), element =>
            ((string?)element.Attribute("Command"))?.Contains("NewTaskCommand", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("Command"))?.Contains("SelectPendingTabCommand", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("Command"))?.Contains("SelectCompletedTabCommand", StringComparison.Ordinal) == true);
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
        Assert.Equal("38", (string?)taskRow.Attribute("Height"));
        Assert.Equal("0,0,0,1", (string?)taskRow.Attribute("BorderThickness"));
        Assert.Contains(taskRow.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding Name}");
        var completedTime = Assert.Single(taskRow.Descendants(Presentation + "TextBlock").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTaskTime"));
        Assert.Equal("Right", (string?)completedTime.Attribute("HorizontalAlignment"));
        Assert.Contains(completedTime.Descendants(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Text" &&
            (string?)setter.Attribute("Value") == "{Binding CompletedTimeDisplay}");
        Assert.Contains(completedTime.Descendants(Presentation + "DataTrigger"), trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsUncompletionRestored}" &&
            trigger.Elements(Presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Text" &&
                (string?)setter.Attribute("Value") == "已恢复"));
        var completedCheck = Assert.Single(taskRow.Descendants(Presentation + "Grid").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTaskCheck"));
        Assert.Equal("CompletedTaskCheck_MouseLeftButtonDown", (string?)completedCheck.Attribute("MouseLeftButtonDown"));
        var completedMore = Assert.Single(taskRow.Descendants(Presentation + "Button").Where(element =>
            (string?)element.Attribute(Xaml + "Name") == "CompletedTaskMore"));
        Assert.Equal("False", (string?)completedMore.Attribute("Focusable"));
        Assert.Equal("False", (string?)completedMore.Attribute("IsTabStop"));
        Assert.Equal("CompletedTaskMore_Click", (string?)completedMore.Attribute("Click"));

        var uncheckingTrigger = Assert.Single(completed.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsUncompleting}" &&
            trigger.Descendants(Presentation + "DoubleAnimation").Any(animation =>
                (string?)animation.Attribute("Storyboard.TargetName") == "CompletedTaskCheckMark")));
        Assert.Contains(uncheckingTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "CompletedTaskTime" &&
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Visible");
        Assert.Contains(uncheckingTrigger.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "CompletedTaskMore" &&
            (string?)setter.Attribute("Property") == "Visibility" &&
            (string?)setter.Attribute("Value") == "Hidden");
        Assert.Equal(2, uncheckingTrigger
            .Elements(Presentation + "DataTrigger.EnterActions")
            .Descendants(Presentation + "DoubleAnimation")
            .Count(animation => (string?)animation.Attribute("Duration") == "0:0:0.12"));

        var exitTrigger = Assert.Single(taskRow.Descendants(Presentation + "DataTrigger").Where(trigger =>
            (string?)trigger.Attribute("Binding") == "{Binding IsUncompletionExiting}"));
        var exitAnimations = exitTrigger
            .Elements(Presentation + "DataTrigger.EnterActions")
            .Descendants(Presentation + "DoubleAnimation")
            .ToArray();
        Assert.Contains(exitAnimations, animation =>
            (string?)animation.Attribute("Storyboard.TargetProperty") == "Opacity" &&
            (string?)animation.Attribute("To") == "0" &&
            (string?)animation.Attribute("Duration") == "0:0:0.2");
        Assert.Contains(exitAnimations, animation =>
            (string?)animation.Attribute("Storyboard.TargetProperty") == "Height" &&
            (string?)animation.Attribute("To") == "0");
        Assert.Contains(exitAnimations, animation =>
            (string?)animation.Attribute("Storyboard.TargetProperty") == "(UIElement.RenderTransform).(TranslateTransform.X)" &&
            (string?)animation.Attribute("To") == "7");
        Assert.DoesNotContain(card.Descendants(Presentation + "ItemsControl"), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding PendingTasks}");
    }

    [Fact]
    public void CompletedModalRendersGroupsAtFixedSizeAndClosesWithEscape()
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
                    { CompletedAtUtc = now.AddMinutes(-index * 60) }));
                data.AddRange(names.Skip(2).Select((name, index) => new LocalTaskDto($"yesterday-{index}", goal.GoalId, name, true, index + 6, now.AddDays(-8), now)
                    { CompletedAtUtc = now.AddDays(-1).AddMinutes(-index * 60) }));
                model.ApplyState(goal, data);
                model.OpenCompletedCommand.Execute(null);

                var modal = new GoalTasksModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 900, Height = 700, Content = modal, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000 };
                window.Show();
                Pump();

                var card = (Border)modal.FindName("GoalTasksCard");
                var completed = (ItemsControl)modal.FindName("CompletedTasksControl");
                Assert.Equal(330, card.ActualWidth);
                Assert.Equal(400, card.ActualHeight);
                Assert.Equal(2, completed.Items.Count);
                Assert.Equal(4, model.PendingCount);
                Assert.Equal(4, model.CompletedCount);
                Assert.Null(modal.FindName("NewTaskButton"));
                Assert.Null(modal.FindName("PendingTasksControl"));
                SavePreview(card);

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
    public void FirstCompletedTaskMenuKeepsScrollOffsetAndUsesItsOwnButtonAsAnchor()
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
                var tasks = Enumerable.Range(0, 15)
                    .Select(index => new LocalTaskDto(
                        $"completed-{index}", goal.GoalId, $"任务 {index + 1}", true, index,
                        now.AddDays(-8), now)
                    {
                        CompletedAtUtc = now.AddMinutes(-index)
                    })
                    .ToArray();
                model.ApplyState(goal, tasks);
                model.OpenCompletedCommand.Execute(null);

                var modal = new GoalTasksModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 900, Height = 700, Content = modal, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000 };
                window.Show();
                Pump();

                var completed = (ItemsControl)modal.FindName("CompletedTasksControl");
                var scrollViewer = (ScrollViewer)modal.FindName("CompletedTasksScrollViewer");
                var menu = (Popup)modal.FindName("CompletedTaskMenu");
                var firstMore = VisualDescendants<Button>(completed)
                    .First(button => button.Name == "CompletedTaskMore");
                firstMore.Visibility = Visibility.Visible;
                scrollViewer.ScrollToTop();
                Pump();
                var offsetBefore = scrollViewer.VerticalOffset;

                firstMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, firstMore));
                Pump();

                Assert.Equal(offsetBefore, scrollViewer.VerticalOffset);
                Assert.True(menu.IsOpen);
                Assert.Same(firstMore, menu.PlacementTarget);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "First completed task menu verification did not finish.");
        Assert.Null(failure);
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

    private static void SavePreview(FrameworkElement card)
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
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, "completed-tasks.png"));
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
