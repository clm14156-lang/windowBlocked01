using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusFlowView
{
    private Storyboard? _entranceStoryboard;
    private Image? _outgoingHome;
    private DispatcherOperation? _snapshotPreparation;
    private DispatcherOperation? _entranceStart;
    private RenderTargetBitmap? _preparedSnapshot;
    private FrameworkElement? _preparedHome;
    private Size _preparedHomeSize;
    private bool _entranceStarted;
    internal Storyboard? EntranceStoryboard => _entranceStoryboard;
    internal bool IsEntranceRunning => _entranceStarted;
    internal BitmapSource? PreparedEntranceSnapshot => _preparedSnapshot;

    internal void PrepareEntranceSnapshot(FrameworkElement home)
    {
        if (_snapshotPreparation is not null || SnapshotMatches(home)) return;
        // Capture near the end of preparation after its text/layout has rendered,
        // keeping this synchronous WPF work outside the entrance animation.
        _snapshotPreparation = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _snapshotPreparation = null;
            if (!home.IsLoaded || !home.IsVisible || home.ActualWidth <= 0 || home.ActualHeight <= 0) return;
            _preparedSnapshot = CaptureHome(home);
            _preparedHome = home;
            _preparedHomeSize = home.RenderSize;
        }));
    }

    private bool SnapshotMatches(FrameworkElement home)
    {
        var dpi = VisualTreeHelper.GetDpi(home);
        return ReferenceEquals(_preparedHome, home) && _preparedHomeSize == home.RenderSize &&
            _preparedSnapshot is { } snapshot &&
            Math.Abs(snapshot.DpiX - dpi.PixelsPerInchX) < 0.001 &&
            Math.Abs(snapshot.DpiY - dpi.PixelsPerInchY) < 0.001 &&
            snapshot.PixelWidth == (int)Math.Ceiling(home.ActualWidth * dpi.DpiScaleX) &&
            snapshot.PixelHeight == (int)Math.Ceiling(home.ActualHeight * dpi.DpiScaleY);
    }

    private static RenderTargetBitmap CaptureHome(FrameworkElement home)
    {
        var dpi = VisualTreeHelper.GetDpi(home);
        var snapshot = new RenderTargetBitmap((int)Math.Ceiling(home.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(home.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        snapshot.Render(home);
        snapshot.Freeze();
        return snapshot;
    }

    internal void PrepareEntranceResources()
    {
        // Preparation lasts five seconds. Decode the selected scene before its first frame,
        // including when this control is still collapsed; reuse the existing character cache.
        GetBindingExpression(FocusThemeScope.IsDarkProperty)?.UpdateTarget();
        FocusCharacter.GetBindingExpression(FocusCharacterImage.CharacterIdProperty)?.UpdateTarget();
        FocusCharacter.GetBindingExpression(FocusCharacterImage.IsNightProperty)?.UpdateTarget();
        FocusCharacter.PrepareSource();
        foreach (var key in new[] { "FocusSceneBackground", "FocusSceneShadow", "FocusSceneContactShadow" })
            if (TryFindResource(key) is BitmapSource bitmap)
            {
                _ = bitmap.PixelWidth;
                if (bitmap.CanFreeze) bitmap.Freeze();
            }
    }

    internal void BeginEntrance(FrameworkElement home, Image outgoingHome)
    {
        var prepared = SnapshotMatches(home) ? _preparedSnapshot : null;
        FinishEntrance();
        PrepareEntranceResources();
        if (!home.IsLoaded || home.ActualWidth <= 0 || home.ActualHeight <= 0) return;

        // Preserve the last rendered preparation frame while its live bindings move to
        // Focusing. The snapshot stays in this HWND and never intercepts window controls.
        _outgoingHome = outgoingHome;
        outgoingHome.Source = prepared ?? CaptureHome(home);
        outgoingHome.Opacity = 1;
        outgoingHome.Visibility = Visibility.Visible;

        var storyboard = new Storyboard { Duration = TimeSpan.FromMilliseconds(420) };
        AddEntranceAnimation(storyboard, outgoingHome, new PropertyPath(OpacityProperty), 1, 0, 0, 220, EasingMode.EaseInOut);
        FadeIn(storyboard, FocusBackground, 0, 420);
        FadeIn(storyboard, FocusGreeting, 80, 340);
        FadeIn(storyboard, FocusLeaves, 120, 300);
        FadeIn(storyboard, FocusCharacterShadowLayers, 120, 300);
        FadeIn(storyboard, FocusCharacterEntrance, 120, 300);
        FadeIn(storyboard, FocusTimerRegion, 100, 320);
        FadeIn(storyboard, FocusTargetRegion, 160, 260);
        FadeIn(storyboard, FocusFooter, 120, 300);
        ((TranslateTransform)FocusCharacterEntrance.RenderTransform).Y = 8;
        AddEntranceAnimation(storyboard, FocusCharacterEntrance,
            new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, TranslateTransform.YProperty), 8, 0, 120, 300);
        _entranceStoryboard = storyboard;
        storyboard.Completed += EntranceCompleted;
        _entranceStart = Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => StartEntranceWhenReadyAsync(storyboard)));
    }

    private async void StartEntranceWhenReadyAsync(Storyboard storyboard)
    {
        _entranceStart = null;
        if (DataContext is FocusSessionViewModel session) await session.EntryGreetingReady;
        if (!ReferenceEquals(_entranceStoryboard, storyboard)) return;
        // Requeue after the greeting's binding/layout updates as well as the scene's
        // first layout. The outgoing snapshot remains opaque until this point.
        _entranceStart = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _entranceStart = null;
            if (!ReferenceEquals(_entranceStoryboard, storyboard)) return;
            if (!IsLoaded || !IsVisible) { FinishEntrance(); return; }
            _entranceStarted = true;
            storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        }));
    }

    private static void FadeIn(Storyboard storyboard, UIElement target, int delay, int duration)
    {
        target.Opacity = 0;
        AddEntranceAnimation(storyboard, target, new PropertyPath(OpacityProperty), 0, 1, delay, duration);
    }

    private static void AddEntranceAnimation(Storyboard storyboard, DependencyObject target, PropertyPath property,
        double from, double to, int delay, int duration, EasingMode easing = EasingMode.EaseOut)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = new CubicEase { EasingMode = easing }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void EntranceCompleted(object? sender, EventArgs args) => FinishEntrance();

    internal void FinishEntrance()
    {
        _snapshotPreparation?.Abort();
        _snapshotPreparation = null;
        _preparedSnapshot = null;
        _preparedHome = null;
        _entranceStart?.Abort();
        _entranceStart = null;
        if (_entranceStoryboard is { } storyboard)
        {
            storyboard.Completed -= EntranceCompleted;
            // Establish final base values before removing the one-shot animation clocks.
            foreach (var target in new UIElement[] { FocusBackground, FocusGreeting, FocusLeaves,
                FocusCharacterShadowLayers, FocusCharacterEntrance, FocusTimerRegion, FocusTargetRegion, FocusFooter })
                target.Opacity = 1;
            ((TranslateTransform)FocusCharacterEntrance.RenderTransform).Y = 0;
            if (_entranceStarted) storyboard.Remove(this);
            _entranceStarted = false;
            _entranceStoryboard = null;
        }
        if (_outgoingHome is not { } outgoingHome) return;
        outgoingHome.Visibility = Visibility.Collapsed;
        outgoingHome.Source = null;
        _outgoingHome = null;
    }
}
