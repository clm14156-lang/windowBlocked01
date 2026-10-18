using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusTaskDrawerInteractionPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTheHoveredOrOpenMenuRowShowsActionsWithoutMovingItsTitle(bool dark)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var target = new FocusTargetViewModel("目标", ["第一个任务", "第二个任务", "一个很长的任务名称用于检查更多按钮显示时不会挤压文字"]);
                var session = new FocusSessionViewModel(runTimer: false);
                session.ApplyFocusTheme(dark ? FocusThemeMode.Dark : FocusThemeMode.Light);
                session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                session.TaskDrawer.ToggleCommand.Execute(null);
                var drawer = new FocusTaskDrawer { DataContext = session.TaskDrawer };
                foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
                    drawer.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
                window = new Window { Content = drawer, Width = 270, Height = 710, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show(); Settle();
                var rows = Descendants<Grid>(drawer).Where(row => row.Name == "TaskRowSurface").ToArray();
                var buttons = rows.Select(row => Descendants<Button>(row).Single(button => button.Name == "TaskMoreButton")).ToArray();
                var title = Descendants<TextBlock>(rows[0]).Single(text => text.Name == "TaskTitle");
                Rect TitleBounds() => new(title.TranslatePoint(new Point(), drawer), new Size(title.ActualWidth, title.ActualHeight));
                var bounds = TitleBounds();
                Assert.All(buttons, button => { Assert.Equal(0, button.Opacity, 2); Assert.False(button.IsHitTestVisible); Assert.Equal(24, button.ActualWidth); });

                SetHover(rows[0], true); Settle();
                Assert.Equal(1, buttons[0].Opacity, 2); Assert.True(buttons[0].IsHitTestVisible);
                Assert.All(buttons.Skip(1), button => Assert.Equal(0, button.Opacity, 2));
                Assert.Equal(((SolidColorBrush)drawer.FindResource("FocusTaskButtonHover")).Color, ((SolidColorBrush)rows[0].Background).Color);
                Assert.Equal(bounds, TitleBounds());
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
                SetHover(rows[0], false); Settle();
                Assert.True(buttons[0].ContextMenu.IsOpen); Assert.Equal(1, buttons[0].Opacity, 2);
                buttons[0].ContextMenu.IsOpen = false; Settle();
                Assert.Equal(0, buttons[0].Opacity, 2); Assert.False(buttons[0].IsHitTestVisible);
                Assert.Equal(bounds, TitleBounds());
                SetHover(rows[1], true); Settle();
                Assert.Equal(1, buttons[1].Opacity, 2); Assert.Equal(0, buttons[0].Opacity, 2);
                SetHover(rows[1], false); Settle();

                var add = (Button)drawer.FindName("DrawerAddTaskButton");
                var addBounds = new Rect(add.TranslatePoint(new Point(), drawer), new Size(add.ActualWidth, add.ActualHeight));
                var surface = (Border)add.Template.FindName("CreateSurface", add);
                Assert.Equal(new Size(42, 42), addBounds.Size); Assert.InRange(addBounds.X, 210, 213); Assert.InRange(addBounds.Y, 648, 651);
                Assert.Equal(new Thickness(0), surface.BorderThickness); Assert.Equal(new CornerRadius(21), surface.CornerRadius);
                Assert.Equal(Color.FromRgb(255, 150, 63), ((SolidColorBrush)surface.Background).Color);
                Assert.IsType<DropShadowEffect>(surface.Effect);
                Assert.Equal(Colors.White, ((SolidColorBrush)Assert.Single(Descendants<System.Windows.Shapes.Path>(surface)).Stroke).Color);
                SetHover(add, true); Settle(); Assert.Equal(Color.FromRgb(245, 135, 46), ((SolidColorBrush)surface.Background).Color);
                Assert.Equal(addBounds, new Rect(add.TranslatePoint(new Point(), drawer), new Size(add.ActualWidth, add.ActualHeight)));
                add.Command.Execute(null); Settle(); Assert.True(session.TaskDrawer.IsCreating);
                session.TaskDrawer.DraftTitle = "新建任务"; session.TaskDrawer.CreateTaskCommand.Execute(null); Settle();
                Assert.Equal("新建任务", target.Tasks.Last().Name); Assert.Equal(4, target.Tasks.Count);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if (failure is not null) throw new InvalidOperationException("Drawer hover verification failed.", failure);
    }

    private static void SetHover(UIElement element, bool value) => element.SetValue(
        (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static void Settle()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
