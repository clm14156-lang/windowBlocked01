using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

/// <summary>A bounded, non-interactive scene layer with one spawn timer and native animation clocks.</summary>
public sealed class FocusLeafLayer : Canvas
{
    public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
        nameof(Sources), typeof(ImageSource[]), typeof(FocusLeafLayer), new PropertyMetadata(null, SourcesChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(FocusLeafLayer), new PropertyMetadata(false, ActivityChanged));
    private readonly Random _random = new();
    private readonly DispatcherTimer _spawnTimer;
    private readonly Dictionary<Image, Storyboard> _leaves = [];
    private Window? _window;
    private Storyboard? _paletteTransition;
    private bool _running;

    public ImageSource[]? Sources { get => (ImageSource[]?)GetValue(SourcesProperty); set => SetValue(SourcesProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    public FocusLeafLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        _spawnTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _spawnTimer.Tick += SpawnTick;
        Loaded += LayerLoaded;
        Unloaded += LayerUnloaded;
        IsVisibleChanged += (_, _) => UpdateActivity();
        SizeChanged += (_, _) => { Stop(); UpdateActivity(); };
    }

    private bool CanRun => IsLoaded && IsActive && IsVisible && Sources is { Length: > 0 } &&
        ActualWidth > 0 && ActualHeight > 0 && _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized;

    private void LayerLoaded(object sender, RoutedEventArgs args)
    {
        DetachWindow();
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += WindowActivityChanged;
            _window.IsVisibleChanged += WindowVisibilityChanged;
            _window.Closed += WindowClosed;
        }
        UpdateActivity();
    }

    private void LayerUnloaded(object sender, RoutedEventArgs args) { Stop(); DetachWindow(); }
    private void WindowActivityChanged(object? sender, EventArgs args) => UpdateActivity();
    private void WindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateActivity();
    private void WindowClosed(object? sender, EventArgs args) { Stop(); DetachWindow(); }
    private void DetachWindow()
    {
        if (_window is null) return;
        _window.StateChanged -= WindowActivityChanged;
        _window.IsVisibleChanged -= WindowVisibilityChanged;
        _window.Closed -= WindowClosed;
        _window = null;
    }

    private static void ActivityChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((FocusLeafLayer)target).UpdateActivity();
    private static void SourcesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var layer = (FocusLeafLayer)target;
        if (layer._running && layer.CanRun) layer.ChangePalette();
        else layer.UpdateActivity();
    }

    private void UpdateActivity()
    {
        if (!CanRun) { Stop(); return; }
        if (_running) return;
        _running = true;
        Schedule(0.4, 1.3);
    }

    private void Schedule(double min, double max)
    {
        if (!CanRun) { Stop(); return; }
        _spawnTimer.Interval = TimeSpan.FromSeconds(min + _random.NextDouble() * (max - min));
        _spawnTimer.Start();
    }

    private void SpawnTick(object? sender, EventArgs args)
    {
        _spawnTimer.Stop();
        if (!CanRun) { Stop(); return; }
        SpawnLeaf();
        Schedule(3, 7);
    }

    private void SpawnLeaf()
    {
        if (!CanRun || _leaves.Count >= 3 || _paletteTransition is not null) return;
        var leaf = new Image
        {
            Source = Sources![_random.Next(Sources.Length)], Stretch = Stretch.Uniform,
            IsHitTestVisible = false, CacheMode = new BitmapCache()
        };
        RenderOptions.SetBitmapScalingMode(leaf, BitmapScalingMode.HighQuality);
        var motion = FocusLeafMotion.Create(_random, leaf, new Size(ActualWidth, ActualHeight));
        motion.Completed += (_, _) => RemoveLeaf(leaf);
        _leaves.Add(leaf, motion);
        Children.Add(leaf);
        motion.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void RemoveLeaf(Image leaf)
    {
        if (!_leaves.Remove(leaf, out var motion)) return;
        motion.Remove(this);
        Children.Remove(leaf);
    }

    private void ClearLeaves()
    {
        foreach (var leaf in _leaves.Keys.ToArray()) RemoveLeaf(leaf);
    }

    private void ChangePalette()
    {
        _spawnTimer.Stop();
        var opacity = Opacity;
        _paletteTransition?.Remove(this);
        // Replace images immediately so the new theme never renders leaves from the old palette.
        foreach (var leaf in _leaves.Keys) leaf.Source = Sources![_random.Next(Sources.Length)];
        if (_leaves.Count == 0) { _paletteTransition = null; Schedule(0.4, 1.3); return; }
        var transition = new Storyboard { Duration = TimeSpan.FromMilliseconds(250), FillBehavior = FillBehavior.Stop };
        var fade = new DoubleAnimation(opacity, 0, transition.Duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        transition.Children.Add(fade);
        transition.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_paletteTransition, transition)) return;
            transition.Remove(this);
            _paletteTransition = null;
            ClearLeaves();
            if (CanRun) Schedule(0.4, 1.3); else Stop();
        };
        _paletteTransition = transition;
        transition.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void Stop()
    {
        _running = false;
        _spawnTimer.Stop();
        _paletteTransition?.Remove(this);
        _paletteTransition = null;
        ClearLeaves();
    }
}
