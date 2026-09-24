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
        Assert.Equal("450", (string?)card.Attribute("Width"));
        Assert.Equal("500", (string?)card.Attribute("Height"));
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "已完成任务");
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "{Binding TodayCompletedSummary}");
        Assert.Contains(card.Descendants(Presentation + "TextBox"), element => ((string?)element.Attribute("Text"))?.Contains("CompletedSearchQuery", StringComparison.Ordinal) == true);
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("Command") == "{Binding ToggleCompletedSortCommand}");
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
        Assert.Equal("42", (string?)taskRow.Attribute("Height"));
        Assert.Equal("0,0,0,1", (string?)taskRow.Attribute("BorderThickness"));
        Assert.Contains(taskRow.Descendants(Presentation + "TextBlock"), element =>
            (string?)element.Attribute("Text") == "{Binding Name}");
        Assert.Contains(taskRow.Descendants(Presentation + "Border"), element => (string?)element.Attribute(Xaml + "Name") == "CompletedIndicator");
        Assert.Contains(taskRow.Descendants(Presentation + "Border"), element => (string?)element.Attribute(Xaml + "Name") == "TaskCheckbox");
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
                Assert.Equal(450, card.ActualWidth);
                Assert.Equal(500, card.ActualHeight);
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
    public void SelectionFooterAndSearchControlsArePresent()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "FocusApp.Desktop", "Views", "GoalTasksModal.xaml"));
        var card = document.Descendants(Presentation + "Border").Single(element => (string?)element.Attribute(Xaml + "Name") == "GoalTasksCard");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("AutomationProperties.Name") == "选择已完成任务");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("AutomationProperties.Name") == "恢复选中");
        Assert.Contains(card.Descendants(Presentation + "Button"), element => (string?)element.Attribute("AutomationProperties.Name") == "删除选中");
        Assert.Contains(card.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == "已选择 ");
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
