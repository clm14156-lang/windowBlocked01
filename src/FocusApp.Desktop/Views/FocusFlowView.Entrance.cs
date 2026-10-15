using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FocusApp.Desktop.Views;

public partial class FocusFlowView
{
    private Storyboard? _entranceStoryboard;
    private Image? _outgoingHome;
    internal Storyboard? EntranceStoryboard => _entranceStoryboard;

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
        FinishEntrance();
        PrepareEntranceResources();
        if (!home.IsLoaded || home.ActualWidth <= 0 || home.ActualHeight <= 0) return;

        // Preserve the last rendered preparation frame while its live bindings move to
        // Focusing. The snapshot stays in this HWND and never intercepts window controls.
        var dpi = VisualTreeHelper.GetDpi(home);
        var snapshot = new RenderTargetBitmap((int)Math.Ceiling(home.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(home.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        snapshot.Render(home);
        snapshot.Freeze();
        _outgoingHome = outgoingHome;
        outgoingHome.Source = snapshot;
        outgoingHome.Visibility = Visibility.Visible;

        var storyboard = new Storyboard { Duration = TimeSpan.FromMilliseconds(420) };
        AddEntranceAnimation(storyboard, outgoingHome, new PropertyPath(OpacityProperty), 1, 0, 0, 150, EasingMode.EaseInOut);
        FadeIn(storyboard, FocusBackground, 0, 420);
        FadeIn(storyboard, FocusGreeting, 120, 300);
        FadeIn(storyboard, FocusLeaves, 180, 240);
        FadeIn(storyboard, FocusCharacterShadowLayers, 180, 240);
        FadeIn(storyboard, FocusCharacterEntrance, 180, 240);
        FadeIn(storyboard, FocusTimerRegion, 240, 180);
        FadeIn(storyboard, FocusTargetRegion, 270, 150);
        FadeIn(storyboard, FocusFooter, 180, 240);
        ((TranslateTransform)FocusCharacterEntrance.RenderTransform).Y = 8;
        AddEntranceAnimation(storyboard, FocusCharacterEntrance,
            new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, TranslateTransform.YProperty), 8, 0, 180, 240);
        _entranceStoryboard = storyboard;
        storyboard.Completed += EntranceCompleted;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
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
        if (_entranceStoryboard is { } storyboard)
        {
            storyboard.Completed -= EntranceCompleted;
            // Establish final base values before removing the one-shot animation clocks.
            foreach (var target in new UIElement[] { FocusBackground, FocusGreeting, FocusLeaves,
                FocusCharacterShadowLayers, FocusCharacterEntrance, FocusTimerRegion, FocusTargetRegion, FocusFooter })
                target.Opacity = 1;
            ((TranslateTransform)FocusCharacterEntrance.RenderTransform).Y = 0;
            storyboard.Remove(this);
            _entranceStoryboard = null;
        }
        if (_outgoingHome is not { } outgoingHome) return;
        outgoingHome.Visibility = Visibility.Collapsed;
        outgoingHome.Source = null;
        _outgoingHome = null;
    }
}
