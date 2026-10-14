using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
public sealed class FocusCharacterMotionTests
{
    [Fact]
    public void NativeClocksFollowTheSmallFourAndFiveSecondCyclesAroundAFixedSeatAnchor()
    {
        Sta(() =>
        {
            var session = Session();
            var view = View(session);
            var host = Host(view);
            var image = (Image)view.FindName("FocusCharacter");
            var shadow = (Image)view.FindName("FocusCharacterShadow");
            try
            {
                host.Show(); Pump(80);
                var state = Assert.IsType<FocusCharacterMotion.MotionState>(FocusCharacterMotion.StateFor(image));
                Assert.True(state.IsRunning);
                var group = Assert.IsType<TransformGroup>(image.RenderTransform);
                var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
                var rotate = Assert.IsType<RotateTransform>(group.Children[1]);
                Assert.Equal(new Point(0.5, 0.85), image.RenderTransformOrigin);
                Assert.Equal(240, image.Width); Assert.Equal(240, image.Height);
                Assert.Equal(new Thickness(0, 332, 0, 0), image.Margin);
                Assert.Equal(Stretch.Uniform, image.Stretch);
                Assert.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(image));
                Assert.Null(image.CacheMode);
                Assert.True(scale.HasAnimatedProperties,
                    $"scale frozen={scale.IsFrozen}; clock={state.Storyboard.GetCurrentState(image)}, time={state.Storyboard.GetCurrentTime(image)}, paused={state.Storyboard.GetIsPaused(image)}");
                Assert.True(rotate.HasAnimatedProperties);
                Assert.Equal(30, Timeline.GetDesiredFrameRate(state.Storyboard));
                Assert.Equal(3, state.Storyboard.Children.Count);
                Assert.All(state.Storyboard.Children, timeline =>
                {
                    var animation = Assert.IsType<DoubleAnimation>(timeline);
                    Assert.True(animation.AutoReverse);
                    Assert.Equal(RepeatBehavior.Forever, animation.RepeatBehavior);
                    Assert.Equal(EasingMode.EaseInOut, Assert.IsType<SineEase>(animation.EasingFunction).EasingMode);
                });
                state.Storyboard.Pause(image); Pump(40);
                var layout = LayoutInformation.GetLayoutSlot(image);
                var seat = new Point(image.ActualWidth * 0.5, image.ActualHeight * 0.85);
                var anchor = image.TranslatePoint(seat, view);
                var shadowLayers = (FrameworkElement)view.FindName("FocusCharacterShadowLayers");
                var shadowLayout = shadowLayers.TransformToVisual(view).TransformBounds(new Rect(shadowLayers.RenderSize));
                var remaining = session.RemainingFocusSeconds;
                foreach (var theme in new[] { FocusThemeMode.Dark, FocusThemeMode.Light })
                {
                    session.ApplyFocusTheme(theme); Pump(30);
                    var source = image.Source;
                    var background = ((Image)view.FindName("FocusBackground")).Source;
                    var shadowSource = shadow.Source;
                    // The two theme PNGs have different aspect ratios; each must stay still during motion.
                    var shadowBounds = shadow.TransformToVisual(view).TransformBounds(new Rect(shadow.RenderSize));
                    Assert.Contains(theme == FocusThemeMode.Dark ? "dark_boy" : "white_boy", source.ToString());
                    foreach (var seconds in new[] { 0d, 1, 2, 2.5, 3, 4, 5, 10, 20, 7200 })
                    {
                        state.Storyboard.SeekAlignedToLastTick(image, TimeSpan.FromSeconds(seconds), TimeSeekOrigin.BeginTime);
                        var time = state.Storyboard.GetCurrentTime(image)!.Value.TotalSeconds;
                        Assert.InRange(Math.Abs(scale.ScaleX - (1 + 0.004 * (1 - Math.Cos(Math.Tau * time / 4)) / 2)), 0, 1e-7);
                        Assert.InRange(Math.Abs(scale.ScaleY - (1 + 0.008 * (1 - Math.Cos(Math.Tau * time / 4)) / 2)), 0, 1e-7);
                        Assert.InRange(Math.Abs(rotate.Angle - (-0.3 * Math.Cos(Math.Tau * time / 5))), 0, 1e-5);
                        var currentAnchor = image.TranslatePoint(seat, view);
                        Assert.InRange((currentAnchor - anchor).Length, 0, 1e-8);
                        Assert.Equal(layout, LayoutInformation.GetLayoutSlot(image));
                        Assert.Equal(shadowBounds, shadow.TransformToVisual(view).TransformBounds(new Rect(shadow.RenderSize)));
                        Assert.Equal(shadowLayout, shadowLayers.TransformToVisual(view).TransformBounds(new Rect(shadowLayers.RenderSize)));
                        Assert.True(shadow.RenderTransform.Value.IsIdentity);
                        Assert.False(shadow.RenderTransform.HasAnimatedProperties);
                        Assert.Same(source, image.Source);
                        Assert.Same(shadowSource, shadow.Source);
                        Assert.Same(background, ((Image)view.FindName("FocusBackground")).Source);
                        if (seconds is 0 or 2 or 2.5 or 20) Save(view, $"character-{theme}-{seconds}");
                    }
                }
                Assert.Equal(remaining, session.RemainingFocusSeconds);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void VisibilitySourceAndSessionChangesPauseOrReuseClocksAndUnloadingRemovesThem()
    {
        Sta(() =>
        {
            var session = Session();
            var view = View(session);
            var host = Host(view);
            var image = (Image)view.FindName("FocusCharacter");
            try
            {
                host.Show(); Pump(80);
                var state = Assert.IsType<FocusCharacterMotion.MotionState>(FocusCharacterMotion.StateFor(image));
                var storyboard = state.Storyboard;
                var transform = image.RenderTransform;
                var initialTime = storyboard.GetCurrentTime(image);
                for (var cycle = 0; cycle < 40; cycle++)
                {
                    session.ApplyFocusTheme(cycle % 2 == 0 ? FocusThemeMode.Light : FocusThemeMode.Dark);
                    Pump(5);
                    Assert.Same(state, FocusCharacterMotion.StateFor(image));
                    Assert.Same(storyboard, state.Storyboard);
                    Assert.Same(transform, image.RenderTransform);
                    Assert.True(state.IsRunning);
                }
                Assert.True(storyboard.GetCurrentTime(image) > initialTime,
                    $"Clock time did not advance: initial={initialTime}, current={storyboard.GetCurrentTime(image)}, state={storyboard.GetCurrentState(image)}, paused={storyboard.GetIsPaused(image)}");
                void AssertPaused()
                {
                    // Pause is processed by the native timing manager; allow its throttled final tick to settle.
                    Pump(200);
                    Assert.True(state.HasClock); Assert.False(state.IsRunning);
                    Assert.True(storyboard.GetIsPaused(image));
                    Assert.Equal(0d, storyboard.GetCurrentGlobalSpeed(image));
                    var pausedPose = transform.Value;
                    Pump(200);
                    // Offscreen windows throttle the root clock, so queried clock time can settle
                    // later than one frame. Verify native pause state and the actual rendered pose.
                    Assert.True(storyboard.GetIsPaused(image));
                    Assert.Equal(0d, storyboard.GetCurrentGlobalSpeed(image));
                    Assert.Equal(pausedPose, transform.Value);
                }
                void AssertResumed()
                {
                    var pausedTime = storyboard.GetCurrentTime(image);
                    Pump(80);
                    Assert.True(state.IsRunning);
                    Assert.False(storyboard.GetIsPaused(image));
                    Assert.True(storyboard.GetCurrentTime(image) > pausedTime);
                    Assert.Same(storyboard, state.Storyboard);
                    Assert.Same(transform, image.RenderTransform);
                }
                host.Hide(); AssertPaused();
                session.ApplyFocusTheme(FocusThemeMode.Light); AssertPaused();
                host.Show(); AssertResumed();
                host.WindowState = WindowState.Minimized; AssertPaused();
                host.WindowState = WindowState.Normal; AssertResumed();
                view.Visibility = Visibility.Collapsed; AssertPaused();
                view.Visibility = Visibility.Visible; AssertResumed();
                var source = image.Source;
                image.SetCurrentValue(Image.SourceProperty, null); AssertPaused();
                image.SetCurrentValue(Image.SourceProperty, source); AssertResumed();
                session.RequestEndCommand.Execute(null);
                session.EndConfirmedFocusCommand.Execute(null); AssertPaused();
                Assert.False(image.IsVisible);
                session.Start(5, null); session.AdvancePreparationBy(TimeSpan.FromSeconds(5)); AssertResumed();
                host.Content = null; Pump(60);
                Assert.False(state.HasClock); Assert.Null(state.HostWindow);
                Assert.False(transform.HasAnimatedProperties);
                Assert.All(((TransformGroup)transform).Children, child => Assert.False(child.HasAnimatedProperties));
                host.Content = view; Pump(80);
                Assert.True(state.IsRunning); Assert.Same(storyboard, state.Storyboard);
                FocusCharacterMotion.SetIsEnabled(image, false); Pump(60);
                Assert.False(state.HasClock); Assert.Null(state.HostWindow);
                Assert.True(image.RenderTransform.Value.IsIdentity);
                FocusCharacterMotion.SetIsEnabled(image, true); Pump(80);
                Assert.True(state.IsRunning); Assert.Same(storyboard, state.Storyboard);
                Assert.Same(transform, image.RenderTransform);
                host.Close(); Pump(60);
                Assert.False(state.HasClock); Assert.Null(state.HostWindow);
                Assert.All(((TransformGroup)transform).Children, child => Assert.False(child.HasAnimatedProperties));
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void RepeatedlyClosedCharacterImagesAndMotionStatesAreCollectible()
    {
        Sta(() =>
        {
            var references = new List<WeakReference>();
            for (var cycle = 0; cycle < 30; cycle++) references.AddRange(CreateAndClose());
            Pump(100);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.All(references, reference => Assert.False(reference.IsAlive));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateAndClose()
    {
        var view = View(Session());
        var host = Host(view);
        host.Show(); Pump(20);
        var image = (Image)view.FindName("FocusCharacter");
        var state = Assert.IsType<FocusCharacterMotion.MotionState>(FocusCharacterMotion.StateFor(image));
        Assert.True(state.IsRunning);
        var references = new[] { new WeakReference(image), new WeakReference(state) };
        host.Close(); host.Content = null; Pump(10);
        Assert.False(state.HasClock); Assert.Null(state.HostWindow);
        return references;
    }

    private static FocusSessionViewModel Session()
    {
        var model = new FocusSessionViewModel(() => new DateTime(2026, 10, 12, 14, 0, 0), false);
        model.Start(25, null); model.AdvancePreparationBy(TimeSpan.FromSeconds(5));
        return model;
    }

    private static FocusFlowView View(FocusSessionViewModel session)
    {
        var view = new FocusFlowView { DataContext = session };
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
        return view;
    }

    private static Window Host(FrameworkElement content) => new()
    {
        Content = content, Width = 800, Height = 710, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000
    };

    private static void Save(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_CHARACTER_QA_PATH");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Character motion UI verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Character motion UI verification failed.", failure);
    }
}
