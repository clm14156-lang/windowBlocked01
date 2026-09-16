using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class GoalTasksModalTests
{
    [Fact]
    public void ModalSupportsTabsInlineInputAndFixedHeaderWhileContentScrolls()
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
                var names = new[] { "完成登录页面", "修复自动屏蔽时间轴", "测试跨日数据", "优化移动端适配", "整理产品需求文档", "阅读技术文章", "准备下周分享", "设计新版图标" };
                var data = names.Select((name, index) => new LocalTaskDto($"task-{index}", goal.GoalId, name, false, index, now.AddDays(-8), now)).ToList();
                data.AddRange(names.Take(4).Select((name, index) => new LocalTaskDto($"completed-{index}", goal.GoalId, name, true, index + 8, now.AddDays(-8), now)
                    { CompletedAtUtc = now.AddMinutes(-index * 60) }));
                data.AddRange(names.Skip(4).Select((name, index) => new LocalTaskDto($"yesterday-{index}", goal.GoalId, name, true, index + 12, now.AddDays(-8), now)
                    { CompletedAtUtc = now.AddDays(-1).AddMinutes(-index * 60) }));
                model.ApplyState(goal, data);
                model.PersistTaskAsync = (task, _) => Task.FromResult<LocalTaskDto?>(task);
                model.OpenCommand.Execute(null);
                var modal = new GoalTasksModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 800, Height = 600, Content = modal, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var card = (Border)modal.FindName("GoalTasksCard");
                var newButton = (Button)modal.FindName("NewTaskButton");
                var editor = (TextBox)modal.FindName("NewTaskNameTextBox");
                var pendingScroll = (ScrollViewer)modal.FindName("PendingTasksScrollViewer");
                Assert.Equal(450, card.ActualWidth);
                Assert.Equal(400, card.ActualHeight);
                Assert.Equal(Visibility.Visible, newButton.Visibility);
                SavePreview(card, "pending");
                model.SelectCompletedTabCommand.Execute(null);
                Pump();
                Assert.True(model.IsCompletedTab);
                Assert.Equal(Visibility.Collapsed, newButton.Visibility);
                SavePreview(card, "completed");
                model.SelectPendingTabCommand.Execute(null);
                Pump();
                InvokeButton(newButton);
                Pump();
                Assert.True(model.IsCreating);
                // The offscreen host stays inactive; verify the focus target without activating the user's window.
                Assert.Same(editor, FocusManager.GetFocusedElement(window));
                InvokeButton(newButton);
                Pump();
                Assert.Equal(8, model.PendingCount);
                SavePreview(card, "editor");
                editor.Text = "学习 UE5 材质";
                PressKey(editor, Key.Enter);
                Pump();
                Assert.Equal(9, model.PendingCount);
                Assert.Equal("学习 UE5 材质", model.PendingTasks[0].Name);
                Assert.False(model.IsCreating);
                model.BeginCreation();
                Pump();
                editor.Text = "取消输入";
                PressKey(editor, Key.Escape);
                Assert.False(model.IsCreating);
                Assert.Equal(9, model.PendingCount);
                model.BeginCreation();
                Pump();
                editor.Text = "失焦保存";
                editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, editor, newButton) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                Pump();
                Assert.Equal("失焦保存", model.PendingTasks[0].Name);
                model.BeginCreation();
                Pump();
                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Assert.False(model.IsCreating);
                Assert.Equal(10, model.PendingCount);
                var before = newButton.TranslatePoint(new Point(), modal);
                pendingScroll.ScrollToBottom();
                Pump();
                Assert.True(pendingScroll.ScrollableHeight > 0);
                Assert.Equal(before, newButton.TranslatePoint(new Point(), modal));
                Assert.Equal(400, card.ActualHeight);
                pendingScroll.ScrollToTop();
                Pump();
                var firstTask = model.PendingTasks[0];
                var checkbox = Descendants<Button>((ItemsControl)modal.FindName("PendingTasksControl")).First();
                InvokeButton(checkbox);
                Pump();
                Assert.Equal(9, model.PendingCount);
                Assert.Equal(9, model.CompletedCount);
                Assert.Contains(model.CompletedGroups[0].Tasks, task => task.TaskId == firstTask.TaskId);
                var closeButton = Descendants<Button>(modal).Single(button => ReferenceEquals(button.Command, model.CloseCommand));
                InvokeButton(closeButton);
                Pump();
                Assert.False(model.IsOpen);
                model.OpenCommand.Execute(null);
                Pump();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Goal task dialog verification did not finish.");
        Assert.Null(failure);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void InvokeButton(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void PressKey(UIElement element, Key key) => element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void SavePreview(FrameworkElement card, string state)
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
        using var stream = File.Create(Path.Combine(directory, $"all-tasks-{state}.png"));
        encoder.Save(stream);
    }
}
