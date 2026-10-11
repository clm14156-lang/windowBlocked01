using System.Collections;
using System.Diagnostics;
using System.Reflection;
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
public sealed class FocusLeafAnimationTests
{
    [Theory]
    [InlineData(800, 710)]
    [InlineData(1070, 710)]
    [InlineData(420, 380)]
    public void IndependentMotionUsesBoundedGentleCurvesAndExitsTheViewport(double width, double height)
    {
        Sta(() =>
        {
            var random = new Random(1024);
            var create = typeof(FocusLeafLayer).Assembly.GetType("FocusApp.Desktop.Views.FocusLeafMotion")!
                .GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
            var signatures = new HashSet<string>();
            for (var index = 0; index < 120; index++)
            {
                var leaf = new Image();
                var motion = (Storyboard)create.Invoke(null, [random, leaf, new Size(width, height)])!;
                var duration = motion.Duration.TimeSpan.TotalSeconds;
                Assert.InRange(duration, 10, 18);
                Assert.InRange(leaf.Width, 20, 38);
                Assert.Equal(leaf.Width, leaf.Height);
                Assert.Equal(30, Timeline.GetDesiredFrameRate(motion));
                var tracks = motion.Children.Cast<DoubleAnimationUsingKeyFrames>().ToArray();
                Assert.Equal(7, tracks.Length);
                var x = tracks[0].KeyFrames.Cast<DoubleKeyFrame>().Select(frame => frame.Value).ToArray();
                var y = tracks[1].KeyFrames.Cast<DoubleKeyFrame>().Select(frame => frame.Value).ToArray();
                Assert.InRange(x[0], width * 0.72, width + leaf.Width);
                Assert.InRange(y[0], -leaf.Height, height * 0.16);
                Assert.True(x[^1] < -leaf.Width - 30);
                Assert.True(y[^1] > height + leaf.Height + 30);
                Assert.All(x.Zip(x.Skip(1)), pair => Assert.True(pair.First > pair.Second));
                Assert.All(y.Zip(y.Skip(1)), pair => Assert.True(pair.First < pair.Second));
                var speeds = x.Zip(x.Skip(1), (first, second) => first - second).ToArray();
                Assert.True(speeds.Max() / speeds.Min() > 1.2);
                var sway = tracks[2].KeyFrames.Cast<DoubleKeyFrame>().Select(frame => frame.Value).ToArray();
                Assert.True(sway.Max() - sway.Min() > 15);
                Assert.All(sway, value => Assert.InRange(value, -36, 36));
                var rotation = tracks[4].KeyFrames.Cast<DoubleKeyFrame>().Select(frame => frame.Value).ToArray();
                Assert.True(rotation.Max() - rotation.Min() > 20);
                Assert.Contains(rotation.Zip(rotation.Skip(1), (first, second) => second - first), delta => delta > 0);
                Assert.Contains(rotation.Zip(rotation.Skip(1), (first, second) => second - first), delta => delta < 0);
                Assert.All(rotation, value => Assert.InRange(value, -81, 81));
                Assert.All(tracks[5].KeyFrames.Cast<DoubleKeyFrame>(), frame => Assert.InRange(frame.Value, 0.8, 1));
                var opacity = tracks[6].KeyFrames;
                Assert.Equal(0, opacity[0].Value);
                Assert.Equal(0, opacity[^1].Value);
                Assert.InRange(opacity[1].Value, 0.65, 0.9);
                Assert.Equal(duration, opacity[^1].KeyTime.TimeSpan.TotalSeconds, 6);
                signatures.Add($"{leaf.Width:R}:{duration:R}:{x[0]:R}:{sway[0]:R}:{rotation[0]:R}");
            }
            Assert.Equal(120, signatures.Count);
        });
    }

    [Fact]
    public void SourcesCapCleanupAndWindowLifecycleDoNotTouchSessionOrInteraction()
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("开发", ["保留任务"]);
            var session = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
            session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5));
            var view = new FocusFlowView { DataContext = session };
            Resources(view);
            var host = new Window { Width = 800, Height = 710, Content = view, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(50);
                var time = session.RemainingFocusSeconds;
                Assert.True(Running(layer));
                Assert.False(layer.IsHitTestVisible);
                Assert.True(layer.ClipToBounds);
                Assert.Equal(view.RenderSize, layer.RenderSize);
                Assert.Null(view.FindName("FocusLeafLeft")); Assert.Null(view.FindName("FocusLeafRight"));
                var names = new[] { "FocusCountdown", "FocusCharacter", "FocusCharacterShadow", "FocusFooterActions" };
                var before = names.ToDictionary(name => name, name => Bounds((FrameworkElement)view.FindName(name), view));
                foreach (var mode in new[] { FocusThemeMode.Dark, FocusThemeMode.Light, FocusThemeMode.Dark })
                {
                    session.ApplyFocusTheme(mode); Pump(400);
                    var dark = mode == FocusThemeMode.Dark;
                    Assert.Equal(dark ? 2 : 3, layer.Sources!.Length);
                    Assert.All(layer.Sources, source => Assert.Contains(dark ? "dark_yezi" : "white_yezi", source.ToString()));
                    for (var attempt = 0; attempt < 12; attempt++) Spawn(layer);
                    Assert.Equal(3, layer.Children.Count);
                    Assert.Equal(3, Motions(layer).Count);
                    Assert.All(layer.Children.Cast<Image>(), leaf => Assert.Contains(leaf.Source, layer.Sources));
                    Assert.All(layer.Children.Cast<Image>(), leaf => Assert.False(leaf.IsHitTestVisible));
                    var phase = 0.18;
                    foreach (var motion in Motions(layer).Values.Cast<Storyboard>().ToArray())
                    {
                        motion.SeekAlignedToLastTick(layer, TimeSpan.FromSeconds(motion.Duration.TimeSpan.TotalSeconds * phase), TimeSeekOrigin.BeginTime);
                        phase += 0.22;
                    }
                    Pump(50);
                    foreach (var leaf in layer.Children.Cast<Image>())
                    {
                        var transforms = ((TransformGroup)leaf.RenderTransform).Children;
                        var drift = Assert.IsType<TranslateTransform>(transforms[3]);
                        Assert.True(drift.HasAnimatedProperties);
                        Assert.InRange(drift.X, 30, layer.ActualWidth);
                        Assert.InRange(drift.Y, 20, layer.ActualHeight);
                        Assert.InRange(leaf.Opacity, 0.6, 0.91);
                        Assert.True(transforms[0].HasAnimatedProperties);
                        Assert.True(transforms[1].HasAnimatedProperties);
                        Assert.True(transforms[2].HasAnimatedProperties);
                    }
                    Save(view, dark ? "focus-leaves-dark" : "focus-leaves-light");
                    session.ApplyFocusTheme(dark ? FocusThemeMode.Light : FocusThemeMode.Dark); Pump(20);
                    Assert.All(layer.Children.Cast<Image>(), leaf => Assert.Contains(leaf.Source, layer.Sources!));
                    Pump(350);
                    Assert.Empty(layer.Children.Cast<Image>());
                    Assert.Empty(Motions(layer));
                }
                ((DispatcherTimer)Field(layer, "_spawnTimer")!).Stop();
                var retiredLeaves = new List<WeakReference>();
                for (var pass = 0; pass < 100; pass++)
                {
                    for (var count = 0; count < 4; count++) Spawn(layer);
                    Assert.InRange(layer.Children.Count, 1, 3);
                    retiredLeaves.AddRange(layer.Children.Cast<Image>().Select(leaf => new WeakReference(leaf)));
                    foreach (var motion in Motions(layer).Values.Cast<Storyboard>().ToArray())
                        motion.SeekAlignedToLastTick(layer, TimeSpan.FromSeconds(20), TimeSeekOrigin.BeginTime);
                    Pump(40);
                    Assert.Empty(layer.Children.Cast<Image>());
                    Assert.Empty(Motions(layer));
                }
                GC.Collect(); GC.WaitForPendingFinalizers(); Pump(50); GC.Collect();
                Assert.InRange(retiredLeaves.Count(reference => reference.IsAlive), 0, 3);
                Spawn(layer);
                host.WindowState = WindowState.Minimized; Pump(50);
                AssertStopped(layer);
                host.WindowState = WindowState.Normal; Pump(50);
                Assert.True(Running(layer));
                Spawn(layer);
                host.Hide(); Pump(50); AssertStopped(layer);
                host.Show(); Pump(50); Assert.True(Running(layer));
                Spawn(layer);
                view.Visibility = Visibility.Collapsed; Pump(50); AssertStopped(layer);
                view.Visibility = Visibility.Visible; Pump(50); Assert.True(Running(layer));
                Spawn(layer);
                foreach (var name in names) Assert.Equal(before[name], Bounds((FrameworkElement)view.FindName(name), view));
                Assert.Equal(time, session.RemainingFocusSeconds);
                Assert.Same(target, session.ActiveTarget); Assert.Single(session.PendingTasks);
                var taskButton = (Button)view.FindName("FocusViewTasksButton");
                taskButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(session.TaskDrawer.IsOpen);
                session.RequestEndCommand.Execute(null); session.EndConfirmedFocusCommand.Execute(null); Pump(50);
                AssertStopped(layer);
                session.Start(30, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); Pump(50);
                Assert.True(Running(layer)); Spawn(layer);
                view.DataContext = null; Pump(50); AssertStopped(layer);
                view.DataContext = session; Pump(50); Assert.True(Running(layer));
                host.Content = null; Pump(50); AssertStopped(layer);
                Assert.Null(Field(layer, "_window"));
                host.Content = view; Pump(50); Assert.True(Running(layer)); Spawn(layer);
                host.Close(); Pump(50); AssertStopped(layer);
                Assert.Null(Field(layer, "_window"));
            }
            finally { host.Close(); }
        });
    }

    private static object? Field(FocusLeafLayer layer, string name) => typeof(FocusLeafLayer).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(layer);
    private static bool Running(FocusLeafLayer layer) => ((DispatcherTimer)Field(layer, "_spawnTimer")!).IsEnabled;
    private static IDictionary Motions(FocusLeafLayer layer) => (IDictionary)Field(layer, "_leaves")!;
    private static void Spawn(FocusLeafLayer layer) => typeof(FocusLeafLayer).GetMethod("SpawnLeaf", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(layer, null);
    private static void AssertStopped(FocusLeafLayer layer) { Assert.False(Running(layer)); Assert.Empty(layer.Children.Cast<Image>()); Assert.Empty(Motions(layer)); }
    private static Rect Bounds(FrameworkElement view, Visual root) => view.TransformToAncestor(root).TransformBounds(new Rect(view.RenderSize));
    private static void Resources(FrameworkElement view)
    {
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Save(FrameworkElement view, string name)
    {
        var path = Environment.GetEnvironmentVariable("FOCUSAPP_LEAF_QA_PATH");
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(path, name + ".png")); png.Save(stream);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Leaf animation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Leaf animation verification failed.", failure);
    }
}
