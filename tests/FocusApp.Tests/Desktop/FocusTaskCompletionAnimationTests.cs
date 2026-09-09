using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public class FocusTaskCompletionAnimationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionIsDelayedAndDuplicateClicksOrUnloadingDoNotToggleItBack(bool unload)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var frame = new DispatcherFrame();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var target = new FocusTargetViewModel("学习", ["任务"]);
                    var model = new FocusSessionViewModel(runTimer: false);
                    model.Start(30, target);
                    var task = target.Tasks[0];
                    var view = new FocusTaskWindow { DataContext = model };
                    var row = new Grid { DataContext = task, Height = 57 };
                    row.Children.Add(new Button { Name = "CompletionCheck" });
                    row.Children.Add(new TextBlock { Name = "CompletionText", Text = task.Name });
                    row.Measure(new Size(240, 57)); row.Arrange(new Rect(0, 0, 240, 57));
                    var method = typeof(FocusTaskWindow).GetMethod("AnimateCompletionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var animation = (Task)method.Invoke(view, [row])!;
                    await (Task)method.Invoke(view, [row])!;
                    Assert.False(task.IsCompleted);
                    await Task.Delay(200);
                    Assert.False(task.IsCompleted);
                    Assert.Equal(TextDecorations.Strikethrough, ((TextBlock)row.Children[1]).TextDecorations);
                    if (unload) view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    await animation;
                    Assert.True(task.IsCompleted);
                    Assert.Single(model.CompletedTasks);
                    Assert.Empty(model.PendingTasks);
                    Assert.True(row.IsHitTestVisible);
                    Assert.Equal(57, row.Height);
                }
                catch (Exception exception) { failure = exception; }
                finally { frame.Continue = false; }
            }));
            Dispatcher.PushFrame(frame);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
