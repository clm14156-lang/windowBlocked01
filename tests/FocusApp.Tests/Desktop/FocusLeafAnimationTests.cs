using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    [Fact]
    public void CalmStopsFramesAndWindowLifecyclePreservesSingleSchedulerAndFocusState()
    {
        Sta(() =>
        {
            var target = new FocusTargetViewModel("开发", ["保留任务"]);
            var session = Session(25, target);
            var view = View(session);
            var host = Host(view, 800, 710);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(80);
                Assert.Equal(1500, layer.TotalFocusSeconds);
                Assert.Equal(FocusWeatherState.Calm, layer.Controller.State);
                Assert.True(layer.IsScheduled); Assert.False(layer.IsRendering);
                var frames = layer.FrameCount;
                Pump(160); Assert.Equal(frames, layer.FrameCount);
                var rule = (FrameworkElement)view.FindName("FocusFooterRule");
                Assert.Equal(Bounds(rule, layer.Parent as Visual ?? view).Top, layer.Viewport.ClipHeight, 5);
                var originalTime = session.RemainingFocusSeconds;
                var elements = new[] { "FocusGreeting", "FocusCenter", "FocusCharacter", "FocusCharacterShadow", "FocusFooterActions" };
                var before = elements.ToDictionary(name => name, name => Bounds((FrameworkElement)view.FindName(name), view));
                SeekState(layer, FocusWeatherState.Breeze);
                Assert.True(layer.IsRendering);
                Pump(180); Assert.True(layer.FrameCount > frames);
                layer.SetCurrentValue(FocusLeafLayer.TotalFocusSecondsProperty, 3600);
                Assert.InRange(layer.Controller.Duration, 30, 60);
                Assert.True(layer.IsScheduled);
                layer.SetCurrentValue(FocusLeafLayer.TotalFocusSecondsProperty, 1500);
                Assert.InRange(layer.Controller.Duration, 20, 40);
                SimulateParticles(layer, 6);
                Assert.InRange(layer.Particles.LeafCount, 1, FocusWeatherParticles.BreezeLeafLimit);
                var state = layer.Controller.State;
                var deadline = layer.Controller.EndsAt;
                host.WindowState = WindowState.Minimized; Pump(80);
                AssertStopped(layer);
                var paused = layer.Now; frames = layer.FrameCount;
                Pump(160); Assert.Equal(paused, layer.Now); Assert.Equal(frames, layer.FrameCount);
                host.WindowState = WindowState.Normal; Pump(100);
                Assert.Equal(state, layer.Controller.State); Assert.Equal(deadline, layer.Controller.EndsAt);
                Assert.True(layer.IsScheduled); Assert.True(layer.IsRendering);
                Assert.Equal(0, layer.Particles.LeafCount);
                for (var cycle = 0; cycle < 12; cycle++)
                {
                    host.Hide(); Pump(10); AssertStopped(layer);
                    host.Show(); Pump(10); Assert.True(layer.IsScheduled);
                }
                view.Visibility = Visibility.Collapsed; Pump(20); AssertStopped(layer);
                view.Visibility = Visibility.Visible; Pump(40); Assert.True(layer.IsScheduled);
                host.Content = null; Pump(30); AssertStopped(layer);
                Assert.Null(WindowReference(layer));
                host.Content = view; Pump(50); Assert.True(layer.IsScheduled);
                Assert.Equal(state, layer.Controller.State);
                foreach (var name in elements) Assert.Equal(before[name], Bounds((FrameworkElement)view.FindName(name), view));
                Assert.Equal(originalTime, session.RemainingFocusSeconds);
                ((Button)view.FindName("FocusViewTasksButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(session.TaskDrawer.IsOpen);

                // Let existing particles finish in Calm; the final empty frame then disables the frame timer.
                SimulateParticles(layer, 6);
                SeekState(layer, FocusWeatherState.Calm);
                Assert.True(layer.IsRendering);
                for (var index = 0; index < layer.Particles.Leaves.Length; index++)
                    if (layer.Particles.Leaves[index].Active) layer.Particles.Leaves[index].Age = layer.Particles.Leaves[index].Lifetime - 0.01;
                Pump(300);
                Assert.False(layer.Particles.HasParticles); Assert.False(layer.IsRendering); Assert.True(layer.IsScheduled);
                frames = layer.FrameCount; Pump(180); Assert.Equal(frames, layer.FrameCount);
                session.RequestEndCommand.Execute(null); session.EndConfirmedFocusCommand.Execute(null); Pump(40);
                AssertStopped(layer);
                session.Start(5, target); session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); Pump(60);
                Assert.Equal(FocusWeatherState.Calm, layer.Controller.State);
                Assert.InRange(layer.Controller.Duration, 30, 60);
                Assert.False(layer.IsRendering);
                view.DataContext = null; Pump(30); AssertStopped(layer);
                view.DataContext = session; Pump(40); Assert.True(layer.IsScheduled);
                host.Close(); Pump(30); AssertStopped(layer); Assert.Null(WindowReference(layer));
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void FirstRainDeadlineUsesTheExistingSchedulerAndSurvivesHidingWithoutBackgroundFrames()
    {
        Sta(() =>
        {
            var session = Session(5, null);
            var view = View(session);
            var host = Host(view, 800, 710);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(50);
                Assert.Equal(300, layer.RemainingFocusSeconds);
                layer.SetCurrentValue(FocusLeafLayer.RemainingFocusSecondsProperty, 70);
                Pump(120);
                Assert.True(layer.Controller.IsPreparingFirstRain);
                Assert.Equal(FocusWeatherState.Calm, layer.Controller.State);
                Assert.True(layer.IsScheduled); Assert.False(layer.IsRendering);
                var scheduler = (DispatcherTimer)typeof(FocusLeafLayer).GetField("_scheduleTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(layer)!;
                Assert.InRange(scheduler.Interval.TotalSeconds, 19, 20.1);
                var deadline = layer.Controller.FirstRainDeadline;
                host.Hide(); Pump(30);
                AssertStopped(layer);
                var paused = layer.Now;
                var frames = layer.FrameCount;
                Pump(100);
                Assert.Equal(paused, layer.Now); Assert.Equal(frames, layer.FrameCount);
                host.Show(); Pump(60);
                Assert.Equal(deadline, layer.Controller.FirstRainDeadline);
                Assert.True(layer.Controller.IsPreparingFirstRain);
                Assert.True(layer.IsScheduled); Assert.False(layer.IsRendering);
                layer.Controller.Advance(layer.Controller.NextChangeAt);
                layer.Particles.BeginState(layer.Controller.State);
                layer.AdvanceState();
                Assert.Equal(FocusWeatherState.Rain, layer.Controller.State);
                Assert.True(layer.IsRendering);
                SimulateParticles(layer, 25);
                Assert.True(layer.Controller.HasShownRain);
                Assert.True(layer.Particles.DropCount > 0);
                Assert.Equal(300, session.RemainingFocusSeconds);
                host.Close(); Pump(20);
                AssertStopped(layer);
            }
            finally { host.Close(); }
        });
    }

    [Theory]
    [InlineData(800, 710)]
    [InlineData(1192, 1060)]
    [InlineData(420, 380)]
    public void ActualDrawingUsesThemeAssetsAndKeepsWeatherAboveFooter(double width, double height)
    {
        Sta(() =>
        {
            var session = Session(25, new FocusTargetViewModel("天气验证", ["任务"]));
            var view = View(session);
            var host = Host(view, width, height);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(60);
                foreach (var theme in new[] { FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(theme); Pump(30);
                    Assert.Equal(theme == FocusThemeMode.Dark ? 2 : 3, layer.Sources!.Length);
                    Assert.All(layer.Sources, source =>
                    {
                        Assert.Contains(theme == FocusThemeMode.Dark ? "dark_yezi" : "white_yezi", source.ToString());
                        Assert.True(source.IsFrozen); Assert.Equal(64, ((BitmapSource)source).PixelWidth);
                    });
                    Assert.False(layer.IsHitTestVisible); Assert.True(layer.ClipToBounds);
                    Assert.Equal(0, VisualTreeHelper.GetChildrenCount(layer));
                    var rainPen = (Pen)typeof(FocusLeafLayer).GetField("_rainPen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(layer)!;
                    var ripplePen = (Pen)typeof(FocusLeafLayer).GetField("_ripplePen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(layer)!;
                    Assert.True(rainPen.IsFrozen); Assert.True(ripplePen.IsFrozen);
                    Assert.Equal(0.33, rainPen.Brush.Opacity, 8); Assert.Equal(0.25, ripplePen.Brush.Opacity, 8);
                    Assert.Equal(1, rainPen.Thickness); Assert.Equal(0.8, ripplePen.Thickness);
                    SeekState(layer, FocusWeatherState.Breeze);
                    SimulateParticles(layer, 6);
                    Save(view, $"weather-{theme}-leaves-{width}");
                    // Restart the isolated weather controller for each preview so cooldown is respected in normal execution.
                    layer.Controller.Start(25, layer.Now);
                    SeekState(layer, FocusWeatherState.Rain);
                    SimulateParticles(layer, 15);
                    Assert.True(layer.Particles.DropCount > 0);
                    var bitmap = Capture(layer);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    // A random snapshot can be empty while new rain is entering a tiny viewport.
                    for (var attempt = 0; attempt < 12 && !pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0); attempt++)
                    {
                        SimulateParticles(layer, 0.5, 15 + attempt * 0.5);
                        bitmap = Capture(layer);
                        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    }
                    Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
                    for (var y = (int)Math.Ceiling(layer.Viewport.ClipHeight); y < bitmap.PixelHeight; y++)
                        for (var x = 0; x < bitmap.PixelWidth; x++) Assert.Equal(0, pixels[(y * bitmap.PixelWidth + x) * 4 + 3]);
                    Save(view, $"weather-{theme}-rain-{width}");
                    layer.Controller.Start(25, layer.Now);
                    SeekState(layer, FocusWeatherState.Calm);
                    layer.Particles.Clear();
                }
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void RainStrokesStayWholeAcrossUiRectangleEdgesBelowUiAndAboveFooter()
    {
        Sta(() =>
        {
            var session = Session(25, new FocusTargetViewModel("雨滴连续性", ["任务"]));
            var view = View(session);
            var host = Host(view, 800, 710);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(60);
                var parent = Assert.IsType<Grid>(layer.Parent);
                Assert.True(parent.Children.IndexOf(layer) < parent.Children.IndexOf((UIElement)view.FindName("FocusingView")));
                Assert.False(layer.IsHitTestVisible);
                var countdown = (FrameworkElement)view.FindName("FocusCountdown");
                var originalLayout = Bounds(countdown, view);
                var originalTime = session.RemainingFocusSeconds;
                foreach (var theme in new[] { FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(theme); Pump(30);
                    layer.Particles.Clear();
                    layer.Controller.Start(25, layer.Now);
                    SeekState(layer, FocusWeatherState.Rain);
                    SimulateParticles(layer, 15);
                    var index = Array.FindIndex(layer.Particles.Drops, drop => drop.Active);
                    Assert.True(index >= 0);
                    foreach (var name in new[] { "FocusGreeting", "FocusCountdown", "FocusTargetRegion", "FocusCharacter", "FocusCharacterShadow" })
                    {
                        var bounds = Bounds((FrameworkElement)view.FindName(name), parent);
                        foreach (var centerY in new[] { bounds.Top, bounds.Top + bounds.Height / 2, bounds.Bottom })
                        {
                            ref var drop = ref layer.Particles.Drops[index];
                            drop.X = Math.Floor(bounds.Left + bounds.Width / 2) + 0.5;
                            drop.Y = Math.Floor(centerY) + 10;
                            drop.Length = 20; drop.Age = 1; drop.Speed = 0; drop.HasGround = false;
                            layer.InvalidateVisual(); Pump(40);
                            var bitmap = Capture(layer);
                            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                            // Every row of this stroke must survive, including either side of the UI edge.
                            for (var y = (int)drop.Y - 19; y < (int)drop.Y; y++)
                                Assert.True(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight, new Rect(drop.X - 1, y, 2, 1)),
                                    $"Rain stroke was cut at row {y} by {name} in {theme}.");
                            Assert.False(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight,
                                new Rect(0, Math.Ceiling(layer.Viewport.ClipHeight), bitmap.PixelWidth, bitmap.PixelHeight - Math.Ceiling(layer.Viewport.ClipHeight))));
                        }
                    }
                }
                Assert.Equal(originalLayout, Bounds(countdown, view));
                Assert.Equal(originalTime, session.RemainingFocusSeconds);
                Assert.Equal(0, VisualTreeHelper.GetChildrenCount(layer));
                ((Button)view.FindName("FocusViewTasksButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(session.TaskDrawer.IsOpen);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void RippleEllipsesStayWholeAcrossUiRectanglesAndResidualRainStopsAfterItsImpactsFade()
    {
        Sta(() =>
        {
            var session = Session(25, new FocusTargetViewModel("涟漪连续性", ["任务"]));
            var view = View(session);
            var host = Host(view, 800, 710);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(60);
                var parent = Assert.IsType<Grid>(layer.Parent);
                Assert.True(parent.Children.IndexOf(layer) < parent.Children.IndexOf((UIElement)view.FindName("FocusingView")));
                Assert.False(layer.IsHitTestVisible);
                foreach (var theme in new[] { FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(theme); Pump(30);
                    layer.Particles.Clear();
                    layer.Controller.Start(25, layer.Now);
                    SeekState(layer, FocusWeatherState.Rain);
                    SimulateParticles(layer, 15);
                    SeekState(layer, FocusWeatherState.Calm);
                    var index = Array.FindIndex(layer.Particles.Ripples, ripple => ripple.Active);
                    Assert.True(index >= 0);
                    foreach (var name in new[] { "FocusGreeting", "FocusCountdown", "FocusTargetRegion", "FocusCharacter", "FocusCharacterShadow" })
                    {
                        var bounds = Bounds((FrameworkElement)view.FindName(name), parent);
                        foreach (var centerY in new[] { bounds.Top, bounds.Top + bounds.Height / 2, bounds.Bottom })
                        {
                            ref var ripple = ref layer.Particles.Ripples[index];
                            ripple.X = bounds.Left + bounds.Width / 2;
                            ripple.Y = centerY;
                            ripple.Age = 0.45; ripple.Lifetime = 1.1; ripple.MaximumRadius = 12;
                            layer.InvalidateVisual(); Pump(40);
                            var bitmap = Capture(layer);
                            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                            for (var point = 0; point < 8; point++)
                            {
                                var angle = point * Math.Tau / 8;
                                var x = ripple.X + ripple.Radius * Math.Cos(angle);
                                var y = ripple.Y + ripple.Radius * FocusWeatherParticles.RippleAspectRatio * Math.Sin(angle);
                                Assert.True(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight, new Rect(x - 1.5, y - 1.5, 3, 3)),
                                    $"Ripple was cut at point {point} by {name} in {theme}.");
                            }
                            Assert.False(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight,
                                new Rect(0, Math.Ceiling(layer.Viewport.ClipHeight), bitmap.PixelWidth, bitmap.PixelHeight - Math.Ceiling(layer.Viewport.ClipHeight))));
                        }
                    }
                }
                foreach (var index in Enumerable.Range(0, layer.Particles.Ripples.Length))
                    if (layer.Particles.Ripples[index].Active)
                        layer.Particles.Ripples[index].Age = layer.Particles.Ripples[index].Lifetime;
                foreach (var index in Enumerable.Range(0, layer.Particles.Drops.Length))
                {
                    ref var drop = ref layer.Particles.Drops[index];
                    if (!drop.Active) continue;
                    Assert.True(layer.Viewport.TryRippleGround(drop.X, out var top, out var bottom));
                    drop.Y = top + (bottom - top) * drop.GroundFraction - 1;
                }
                Assert.True(layer.Particles.DropCount > 0);
                Pump(100);
                Assert.Equal(FocusWeatherState.Calm, layer.Controller.State);
                Assert.Equal(0, layer.Particles.DropCount);
                Assert.True(layer.Particles.RippleCount > 0);
                Assert.True(layer.IsRendering);
                Pump(1300);
                Assert.False(layer.Particles.HasParticles);
                Assert.False(layer.IsRendering);
                Assert.True(layer.IsScheduled);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void LeavesRenderContinuouslyAcrossUiRectanglesBelowUiAndAboveFooter()
    {
        Sta(() =>
        {
            var session = Session(25, new FocusTargetViewModel("落叶连续性", ["任务"]));
            var view = View(session);
            var host = Host(view, 800, 710);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(60);
                var parent = Assert.IsType<Grid>(layer.Parent);
                Assert.True(parent.Children.IndexOf(layer) < parent.Children.IndexOf((UIElement)view.FindName("FocusingView")));
                Assert.False(layer.IsHitTestVisible);
                var countdown = (FrameworkElement)view.FindName("FocusCountdown");
                var originalLayout = Bounds(countdown, view);
                var originalTime = session.RemainingFocusSeconds;
                foreach (var theme in new[] { FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(theme); Pump(30);
                    layer.Particles.Clear();
                    SeekState(layer, FocusWeatherState.Breeze);
                    SimulateParticles(layer, 3);
                    SeekState(layer, FocusWeatherState.Calm);
                    var index = Array.FindIndex(layer.Particles.Leaves, leaf => leaf.Active);
                    Assert.True(index >= 0);
                    foreach (var name in new[] { "FocusGreeting", "FocusCountdown", "FocusTargetRegion", "FocusCharacter", "FocusCharacterShadow" })
                    {
                        var bounds = Bounds((FrameworkElement)view.FindName(name), parent);
                        ref var leaf = ref layer.Particles.Leaves[index];
                        leaf.X = bounds.Left + bounds.Width / 2;
                        leaf.Y = bounds.Top + bounds.Height / 2;
                        leaf.Size = 32; leaf.Age = 2; leaf.Lifetime = 18;
                        leaf.SpeedX = leaf.SpeedY = leaf.Sway = leaf.Tilt = 0;
                        leaf.BoundaryOpacity = 1;
                        layer.InvalidateVisual(); Pump(40);
                        var bitmap = Capture(layer);
                        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                        Assert.True(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight, bounds), $"Leaf was clipped by {name} in {theme}.");
                        Assert.False(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight,
                            new Rect(0, Math.Ceiling(layer.Viewport.ClipHeight), bitmap.PixelWidth, bitmap.PixelHeight - Math.Ceiling(layer.Viewport.ClipHeight))));
                        Save(view, $"leaf-ui-{theme}-{name}");
                    }
                }
                Assert.Equal(originalLayout, Bounds(countdown, view));
                Assert.Equal(originalTime, session.RemainingFocusSeconds);
                Assert.Equal(0, VisualTreeHelper.GetChildrenCount(layer));
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void NativeLeafDrawingFadesToEmptyBeforeCrossingTheFooter()
    {
        Sta(() =>
        {
            var view = View(Session(25, null));
            var host = Host(view, 800, 710);
            var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
            try
            {
                host.Show(); Pump(60);
                SeekState(layer, FocusWeatherState.Breeze);
                SimulateParticles(layer, 3);
                SeekState(layer, FocusWeatherState.Calm);
                var index = Array.FindIndex(layer.Particles.Leaves, leaf => leaf.Active);
                Assert.True(index >= 0);
                long previousAlpha = long.MaxValue;
                foreach (var gap in new[] { 60, 50, 40, 25, 10, 0 })
                {
                    ref var leaf = ref layer.Particles.Leaves[index];
                    leaf.Age = 2; leaf.Lifetime = 18; leaf.Size = 34;
                    leaf.X = 120; leaf.Y = layer.Viewport.ClipHeight - leaf.Size * 0.7071067811865476 - gap;
                    leaf.SpeedX = leaf.SpeedY = leaf.Sway = leaf.Tilt = 0;
                    leaf.BoundaryOpacity = 1;
                    layer.InvalidateVisual(); Pump(80);
                    var bitmap = Capture(layer);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    long alpha = 0;
                    for (var pixel = 3; pixel < pixels.Length; pixel += 4) alpha += pixels[pixel];
                    // Two fully opaque poses can differ slightly because of subpixel image resampling.
                    if (gap < 50) Assert.True(alpha <= previousAlpha);
                    if (gap == 60) Assert.True(alpha > 0);
                    Assert.False(HasInk(pixels, bitmap.PixelWidth, bitmap.PixelHeight,
                        new Rect(0, Math.Ceiling(layer.Viewport.ClipHeight), bitmap.PixelWidth, bitmap.PixelHeight - Math.Ceiling(layer.Viewport.ClipHeight))));
                    Save(view, $"leaf-boundary-gap-{gap}");
                    previousAlpha = alpha;
                }
                Assert.Equal(0, previousAlpha);
                Assert.False(layer.Particles.HasParticles);
                Assert.False(layer.IsRendering);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void ClosedWeatherLayersAreCollectibleAfterRepeatedEntry()
    {
        Sta(() =>
        {
            var references = new List<WeakReference>();
            for (var cycle = 0; cycle < 30; cycle++) references.Add(CreateAndClose());
            Pump(100);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.All(references, reference => Assert.False(reference.IsAlive));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndClose()
    {
        var view = View(Session(5, null));
        var host = Host(view, 800, 710);
        host.Show(); Pump(15);
        var layer = (FocusLeafLayer)view.FindName("FocusLeaves");
        SeekState(layer, FocusWeatherState.Breeze); SimulateParticles(layer, 4);
        var reference = new WeakReference(layer);
        host.Close(); host.Content = null;
        AssertStopped(layer);
        return reference;
    }

    private static FocusSessionViewModel Session(int minutes, FocusTargetViewModel? target)
    {
        var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
        model.Start(minutes, target); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        return model;
    }

    private static FocusFlowView View(FocusSessionViewModel session)
    {
        var view = new FocusFlowView { DataContext = session };
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
        return view;
    }

    private static Window Host(FrameworkElement content, double width, double height) => new()
    {
        Content = content, Width = width, Height = height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000
    };

    private static void SeekState(FocusLeafLayer layer, FocusWeatherState state)
    {
        for (var attempt = 0; attempt < 512 && layer.Controller.State != state; attempt++)
        {
            if (layer.Controller.State == FocusWeatherState.Rain)
                for (var tick = 1; tick <= 101; tick++)
                    layer.Controller.ObserveRain(0.1, layer.Controller.StartedAt + layer.Controller.RainRampSeconds + tick / 10d);
            layer.Controller.Advance(layer.Controller.NextChangeAt);
            layer.Particles.BeginState(layer.Controller.State);
        }
        layer.AdvanceState();
        Assert.Equal(state, layer.Controller.State);
    }

    private static void SimulateParticles(FocusLeafLayer layer, double seconds, double ageOffset = 0)
    {
        for (var tick = 1; tick <= seconds * 30; tick++)
            layer.Particles.Update(1d / 30, layer.Controller.StartedAt + ageOffset + tick / 30d, layer.Controller, layer.Viewport, layer.Sources!.Length);
        layer.InvalidateVisual(); Pump(35);
    }

    private static object? WindowReference(FocusLeafLayer layer) => typeof(FocusLeafLayer).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(layer);
    private static void AssertStopped(FocusLeafLayer layer) { Assert.False(layer.IsRendering); Assert.False(layer.IsScheduled); Assert.False(layer.Particles.HasParticles); }
    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToVisual(root).TransformBounds(new Rect(element.RenderSize));
    private static bool HasInk(byte[] pixels, int width, int height, Rect bounds)
    {
        for (var y = Math.Max(0, (int)Math.Ceiling(bounds.Top)); y < Math.Min(height, (int)bounds.Bottom); y++)
            for (var x = Math.Max(0, (int)Math.Ceiling(bounds.Left)); x < Math.Min(width, (int)bounds.Right); x++)
                if (pixels[(y * width + x) * 4 + 3] > 0) return true;
        return false;
    }
    private static RenderTargetBitmap Capture(FrameworkElement view)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); return bitmap;
    }
    private static void Save(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_WEATHER_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(Capture(view)));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); png.Save(stream);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Weather UI verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Weather UI verification failed.", failure);
    }
}
