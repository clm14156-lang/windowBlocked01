using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FocusApp.Desktop.Views;

/// <summary>One reusable native storyboard per character Image; PNG changes retain its clocks and pose.</summary>
public static class FocusCharacterMotion
{
    // All motion parameters live here. IsEnabled can disable the effect on any character Image.
    internal const double BreathScaleX = 1.004, BreathScaleY = 1.008;
    internal const double SwayMinimum = -0.3, SwayMaximum = 0.3;
    internal const double BreathCycleSeconds = 4, SwayCycleSeconds = 5;
    internal const int FrameRate = 30;
    public static Point TransformOrigin { get; } = new(0.5, 0.85);

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(FocusCharacterMotion), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(MotionState), typeof(FocusCharacterMotion));
    private static readonly DependencyProperty ObservedSourceProperty = DependencyProperty.RegisterAttached(
        "ObservedSource", typeof(ImageSource), typeof(FocusCharacterMotion), new PropertyMetadata(null, SourceChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    internal static MotionState? StateFor(Image image) => (MotionState?)image.GetValue(StateProperty);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Image image) return;
        var state = StateFor(image);
        if (state is null && (bool)args.NewValue)
        {
            state = new MotionState(image);
            image.SetValue(StateProperty, state);
        }
        state?.SetEnabled((bool)args.NewValue);
    }

    private static void SourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is Image image) StateFor(image)?.UpdateActivity();
    }

    internal sealed class MotionState
    {
        private readonly Image _image;
        private readonly Transform _originalTransform;
        private readonly TransformGroup _transform;
        private bool _enabled, _paused, _observingSource;
        private Window? _window;
        internal Storyboard Storyboard { get; } = new();
        internal bool HasClock { get; private set; }
        internal bool IsRunning => HasClock && !_paused;
        internal Window? HostWindow => _window;

        internal MotionState(Image image)
        {
            _image = image;
            _originalTransform = image.RenderTransform;
            var scale = new ScaleTransform(1, 1);
            var rotate = new RotateTransform(SwayMinimum);
            _transform = new TransformGroup();
            _transform.Children.Add(scale);
            _transform.Children.Add(rotate);
            AddAnimation(0, ScaleTransform.ScaleXProperty, 1, BreathScaleX, BreathCycleSeconds);
            AddAnimation(0, ScaleTransform.ScaleYProperty, 1, BreathScaleY, BreathCycleSeconds);
            AddAnimation(1, RotateTransform.AngleProperty, SwayMinimum, SwayMaximum, SwayCycleSeconds);
            Timeline.SetDesiredFrameRate(Storyboard, FrameRate);
        }

        private void AddAnimation(int transformIndex, DependencyProperty property, double from, double to, double cycleSeconds)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(cycleSeconds / 2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(animation, _image);
            // Target through the Image so the timeline snapshot cannot animate a detached Freezable copy.
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation,
                new PropertyPath($"(0).(1)[{transformIndex}].(2)", UIElement.RenderTransformProperty, TransformGroup.ChildrenProperty, property));
            Storyboard.Children.Add(animation);
        }

        internal void SetEnabled(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            if (enabled)
            {
                _image.RenderTransform = _transform;
                _image.Loaded += Loaded;
                _image.Unloaded += Unloaded;
                _image.IsVisibleChanged += VisibilityChanged;
                if (_image.IsLoaded) AttachWindow();
                UpdateActivity();
            }
            else
            {
                ReleaseClockAndHost();
                _image.Loaded -= Loaded;
                _image.Unloaded -= Unloaded;
                _image.IsVisibleChanged -= VisibilityChanged;
                _image.RenderTransform = _originalTransform;
            }
        }

        private void Loaded(object sender, RoutedEventArgs args) { AttachWindow(); UpdateActivity(); }
        private void Unloaded(object sender, RoutedEventArgs args) => ReleaseClockAndHost();
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateActivity();
        private void WindowStateChanged(object? sender, EventArgs args) => UpdateActivity();
        private void WindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateActivity();
        private void WindowClosed(object? sender, EventArgs args) => ReleaseClockAndHost();

        private void AttachWindow()
        {
            DetachWindow();
            _window = Window.GetWindow(_image);
            if (_window is not null)
            {
                _window.StateChanged += WindowStateChanged;
                _window.IsVisibleChanged += WindowVisibilityChanged;
                _window.Closed += WindowClosed;
            }
            if (!_observingSource)
            {
                _observingSource = true;
                // Observe the existing source with a local binding; no global descriptor handler retains the Image.
                BindingOperations.SetBinding(_image, ObservedSourceProperty, new Binding(nameof(Image.Source)) { Source = _image });
            }
        }

        private void DetachWindow()
        {
            if (_window is null) return;
            _window.StateChanged -= WindowStateChanged;
            _window.IsVisibleChanged -= WindowVisibilityChanged;
            _window.Closed -= WindowClosed;
            _window = null;
        }

        internal void UpdateActivity()
        {
            var canRun = _enabled && _image.IsLoaded && _image.IsVisible && _image.Source is not null &&
                _window is { IsVisible: true, WindowState: not WindowState.Minimized };
            if (canRun)
            {
                if (!HasClock)
                {
                    Storyboard.Begin(_image, HandoffBehavior.SnapshotAndReplace, true);
                    HasClock = true;
                    _paused = false;
                }
                else if (_paused)
                {
                    Storyboard.Resume(_image);
                    _paused = false;
                }
            }
            else if (HasClock && !_paused)
            {
                Storyboard.Pause(_image);
                _paused = true;
            }
        }

        private void ReleaseClockAndHost()
        {
            if (HasClock) Storyboard.Remove(_image);
            HasClock = false;
            _paused = false;
            DetachWindow();
            if (_observingSource)
            {
                _observingSource = false;
                BindingOperations.ClearBinding(_image, ObservedSourceProperty);
            }
        }
    }
}
