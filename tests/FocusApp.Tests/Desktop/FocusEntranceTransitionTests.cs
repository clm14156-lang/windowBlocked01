using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class FocusEntranceTransitionTests
{
    [Fact]
    public void NativeEntranceStagesBothThemesAndReleasesClocksWithoutChangingFocusOrBreathing()
    {
        RunSta(() =>
        {
            foreach (var night in new[] { false, true })
            {
                var session = new FocusSessionViewModel(runTimer: false);
                session.ApplyFocusTheme(night ? FocusThemeMode.Dark : FocusThemeMode.Light);
                session.ApplyFocusCharacter("white_littlefox");
                session.Start(5, new FocusTargetViewModel("进入专注", ["任务一"]));
                var root = new Grid();
                foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
                    root.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
                var home = new Border { Background = Brushes.LightGray, Child = new TextBlock { Text = "准备开始专注" } };
                var view = new FocusFlowView { DataContext = session, Visibility = Visibility.Collapsed };
                var outgoing = new Image { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                root.Children.Add(home); root.Children.Add(view); root.Children.Add(outgoing);
                var host = new Window { Content = root, Width = 800, Height = 710, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                try
                {
                    host.Show(); Pump(40);
                    view.PrepareEntranceResources();
                    var character = (Image)view.FindName("FocusCharacter");
                    var decoded = Assert.IsType<BitmapImage>(character.Source);
                    Assert.True(decoded.IsFrozen);
                    Assert.Contains(night ? "dark_littlefox" : "white_littlefox", decoded.UriSource.ToString());
                    session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
                    view.BeginEntrance(home, outgoing);
                    view.Visibility = Visibility.Visible;
                    Pump(30);
                    var storyboard = Assert.IsType<Storyboard>(view.EntranceStoryboard);
                    storyboard.Pause(view);
                    Assert.Equal(TimeSpan.FromMilliseconds(420), storyboard.Duration.TimeSpan);
                    Assert.All(storyboard.Children, timeline =>
                        Assert.IsType<CubicEase>(Assert.IsType<DoubleAnimation>(timeline).EasingFunction));
                    Assert.Same(decoded, character.Source);
                    Assert.True(Assert.IsType<RenderTargetBitmap>(outgoing.Source).IsFrozen);
                    Assert.Equal(Visibility.Visible, outgoing.Visibility);
                    Assert.False(outgoing.IsHitTestVisible);
                    Assert.Same(host, Window.GetWindow(view));
                    var background = (Image)view.FindName("FocusBackground");
                    var entrance = (Grid)view.FindName("FocusCharacterEntrance");
                    var translate = Assert.IsType<TranslateTransform>(entrance.RenderTransform);
                    var timer = (FrameworkElement)view.FindName("FocusTimerRegion");
                    var target = (FrameworkElement)view.FindName("FocusTargetRegion");
                    void Seek(int milliseconds) => storyboard.SeekAlignedToLastTick(view,
                        TimeSpan.FromMilliseconds(milliseconds), TimeSeekOrigin.BeginTime);
                    Seek(0);
                    Assert.Equal(1, outgoing.Opacity);
                    Assert.Equal(0, background.Opacity);
                    Seek(90);
                    Assert.InRange(outgoing.Opacity, 0.1, 0.9);
                    Assert.InRange(background.Opacity, 0.1, 0.9);
                    Assert.Equal(0, entrance.Opacity); Assert.Equal(8, translate.Y);
                    Seek(179);
                    Assert.Equal(0, outgoing.Opacity);
                    Assert.Equal(0, entrance.Opacity); Assert.Equal(8, translate.Y);
                    Seek(210);
                    Assert.InRange(entrance.Opacity, 0.1, 0.9);
                    Assert.InRange(translate.Y, 0.1, 7.9);
                    Assert.Equal(0, timer.Opacity); Assert.Equal(0, target.Opacity);
                    Seek(255);
                    Assert.InRange(timer.Opacity, 0.01, 0.99); Assert.Equal(0, target.Opacity);
                    Seek(330);
                    Assert.InRange(target.Opacity, 0.1, 0.99);
                    Assert.Equal(300, session.RemainingFocusSeconds);
                    session.AdvanceOneSecond();
                    Assert.Equal(299, session.RemainingFocusSeconds);
                    Assert.Same(storyboard, view.EntranceStoryboard);
                    session.TaskDrawer.ToggleCommand.Execute(null);
                    Assert.True(session.TaskDrawer.IsOpen);
                    var breathing = Assert.IsType<FocusCharacterMotion.MotionState>(FocusCharacterMotion.StateFor(character));
                    Assert.True(breathing.IsRunning);
                    storyboard.Resume(view); Seek(440); Pump(60);
                    Assert.Null(view.EntranceStoryboard);
                    Assert.Null(outgoing.Source); Assert.Equal(Visibility.Collapsed, outgoing.Visibility);
                    Assert.Equal(1, background.Opacity); Assert.False(background.HasAnimatedProperties);
                    Assert.Equal(1, entrance.Opacity); Assert.False(entrance.HasAnimatedProperties);
                    Assert.Equal(0, translate.Y); Assert.False(translate.HasAnimatedProperties);
                    Assert.Equal(1, timer.Opacity); Assert.Equal(1, target.Opacity);
                    Assert.True(breathing.IsRunning);
                    host.Hide(); host.Show(); Pump(30);
                    Assert.Null(view.EntranceStoryboard);
                    view.BeginEntrance(home, outgoing);
                    Pump(20); host.Hide();
                    Assert.Null(view.EntranceStoryboard); Assert.Null(outgoing.Source);
                    Assert.Equal(0, translate.Y); Assert.Equal(1, entrance.Opacity);
                    host.Show(); Pump(20);
                    Assert.Null(view.EntranceStoryboard);
                    view.BeginEntrance(home, outgoing);
                    Pump(20); host.Content = null; Pump(20);
                    Assert.Null(view.EntranceStoryboard); Assert.Null(outgoing.Source);
                    Assert.False(translate.HasAnimatedProperties);
                }
                finally { host.Close(); }
            }
        });
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Focus entrance verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Focus entrance verification failed.", failure);
    }
}
