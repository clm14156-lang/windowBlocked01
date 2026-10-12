using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

/// <summary>Retains the existing scene-layer name; draws all weather in one non-interactive visual.</summary>
public sealed class FocusLeafLayer : FrameworkElement
{
    public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
        nameof(Sources), typeof(ImageSource[]), typeof(FocusLeafLayer), new PropertyMetadata(null, SourcesChanged));
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(FocusLeafLayer), new PropertyMetadata(false, ActivityChanged));
    public static readonly DependencyProperty TotalFocusSecondsProperty = DependencyProperty.Register(
        nameof(TotalFocusSeconds), typeof(int), typeof(FocusLeafLayer), new PropertyMetadata(1500, DurationChanged));
    public static readonly DependencyProperty RemainingFocusSecondsProperty = DependencyProperty.Register(
        nameof(RemainingFocusSeconds), typeof(int), typeof(FocusLeafLayer), new PropertyMetadata(int.MaxValue, RemainingChanged));
    public static readonly DependencyProperty WeatherBrushProperty = DependencyProperty.Register(
        nameof(WeatherBrush), typeof(Brush), typeof(FocusLeafLayer), new PropertyMetadata(Brushes.LightSteelBlue, BrushChanged));

    private readonly DispatcherTimer _scheduleTimer;
    private readonly DispatcherTimer _frameTimer;
    private readonly Stopwatch _visibleClock = new();
    private readonly MatrixTransform[] _leafTransforms = new MatrixTransform[FocusWeatherParticles.MaximumLeaves];
    private Window? _window;
    private Image? _background;
    private FrameworkElement? _rule, _footer;
    private Geometry? _sceneClip;
    private Pen _rainPen = null!, _ripplePen = null!;
    private bool _sessionStarted, _running;
    private double _lastFrameTime;

    internal FocusWeatherController Controller { get; }
    internal FocusWeatherParticles Particles { get; }
    internal WeatherViewport Viewport { get; private set; }
    internal bool IsRendering => _frameTimer.IsEnabled;
    internal bool IsScheduled => _scheduleTimer.IsEnabled;
    internal long FrameCount { get; private set; }
    internal double Now => _visibleClock.Elapsed.TotalSeconds;

    public ImageSource[]? Sources { get => (ImageSource[]?)GetValue(SourcesProperty); set => SetValue(SourcesProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public int TotalFocusSeconds { get => (int)GetValue(TotalFocusSecondsProperty); set => SetValue(TotalFocusSecondsProperty, value); }
    public int RemainingFocusSeconds { get => (int)GetValue(RemainingFocusSecondsProperty); set => SetValue(RemainingFocusSecondsProperty, value); }
    public Brush WeatherBrush { get => (Brush)GetValue(WeatherBrushProperty); set => SetValue(WeatherBrushProperty, value); }

    public FocusLeafLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        var random = new Random();
        Controller = new FocusWeatherController(random);
        Particles = new FocusWeatherParticles(random);
        for (var i = 0; i < _leafTransforms.Length; i++) _leafTransforms[i] = new MatrixTransform();
        _scheduleTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _scheduleTimer.Tick += ScheduleTick;
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromSeconds(1d / 30) };
        _frameTimer.Tick += FrameTick;
        UpdatePens();
        Loaded += LayerLoaded;
        Unloaded += LayerUnloaded;
        IsVisibleChanged += (_, _) => UpdateActivity();
        LayoutUpdated += (_, _) => UpdateSceneBounds();
    }

    internal void SetScene(Image background, FrameworkElement rule, FrameworkElement footer)
    {
        _background = background; _rule = rule; _footer = footer;
    }

    private bool CanRun => IsLoaded && IsActive && IsVisible && ActualWidth > 0 && ActualHeight > 0 &&
        _window is { IsVisible: true } && _window.WindowState != WindowState.Minimized;

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
        UpdateSceneBounds();
        UpdateActivity();
    }

    private void LayerUnloaded(object sender, RoutedEventArgs args) { Suspend(); DetachWindow(); }
    private void WindowActivityChanged(object? sender, EventArgs args) => UpdateActivity();
    private void WindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => UpdateActivity();
    private void WindowClosed(object? sender, EventArgs args) { StopSession(); DetachWindow(); }
    private void DetachWindow()
    {
        if (_window is null) return;
        _window.StateChanged -= WindowActivityChanged;
        _window.IsVisibleChanged -= WindowVisibilityChanged;
        _window.Closed -= WindowClosed;
        _window = null;
    }

    private static void ActivityChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((FocusLeafLayer)target).UpdateActivity();
    private static void DurationChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var layer = (FocusLeafLayer)target;
        if (!layer._sessionStarted) return;
        layer.Controller.ChangeFocusDuration(layer.TotalFocusSeconds / 60d, layer.Now);
        layer.Controller.LimitFirstRainToRemainingTime(layer.Now, layer.RemainingFocusSeconds);
        if (layer._running) layer.ScheduleNextState();
    }

    private static void RemainingChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var layer = (FocusLeafLayer)target;
        if (layer._running && layer.Controller.LimitFirstRainToRemainingTime(layer.Now, layer.RemainingFocusSeconds))
            layer.ScheduleNextState();
    }

    private static void SourcesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var layer = (FocusLeafLayer)target;
        if (layer.Sources is { } sources)
            foreach (var source in sources) if (source.CanFreeze && !source.IsFrozen) source.Freeze();
        // Draw the new shared palette without resetting the weather, timers or individual ages.
        if (layer.Particles.HasParticles) layer.InvalidateVisual();
    }

    private static void BrushChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var layer = (FocusLeafLayer)target;
        layer.UpdatePens();
        if (layer.Particles.HasParticles) layer.InvalidateVisual();
    }

    private void UpdatePens()
    {
        var rainBrush = WeatherBrush.CloneCurrentValue();
        rainBrush.Opacity *= 0.33;
        rainBrush.Freeze();
        var rippleBrush = WeatherBrush.CloneCurrentValue();
        rippleBrush.Opacity *= 0.25;
        rippleBrush.Freeze();
        _rainPen = new Pen(rainBrush, 1); _rainPen.Freeze();
        _ripplePen = new Pen(rippleBrush, 0.8); _ripplePen.Freeze();
    }

    private void UpdateActivity()
    {
        if (!IsActive) { StopSession(); return; }
        if (!CanRun) { Suspend(); return; }
        UpdateSceneBounds();
        if (_running) return;
        _running = true;
        if (!_sessionStarted)
        {
            _visibleClock.Reset();
            Controller.Start(TotalFocusSeconds / 60d, 0);
            Particles.BeginState(Controller.State);
            _sessionStarted = true;
        }
        _visibleClock.Start();
        Controller.LimitFirstRainToRemainingTime(Now, RemainingFocusSeconds);
        ScheduleNextState();
        UpdateRendering();
    }

    private void ScheduleNextState()
    {
        _scheduleTimer.Stop();
        _scheduleTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.05, Controller.NextChangeAt - Now));
        _scheduleTimer.Start();
    }

    private void ScheduleTick(object? sender, EventArgs args)
    {
        _scheduleTimer.Stop();
        if (!CanRun) { UpdateActivity(); return; }
        AdvanceState();
    }

    internal void AdvanceState()
    {
        if (Controller.Advance(Now)) Particles.BeginState(Controller.State);
        if (!_running) return;
        ScheduleNextState();
        UpdateRendering();
    }

    private void UpdateRendering()
    {
        var needsFrames = _running && (Controller.State != FocusWeatherState.Calm || Particles.HasParticles);
        if (needsFrames && !_frameTimer.IsEnabled)
        {
            _lastFrameTime = Now;
            _frameTimer.Start();
        }
        else if (!needsFrames) _frameTimer.Stop();
    }

    private void FrameTick(object? sender, EventArgs args)
    {
        if (!CanRun) { UpdateActivity(); return; }
        var now = Now;
        Particles.Update(now - _lastFrameTime, now, Controller, Viewport, Sources?.Length ?? 0);
        _lastFrameTime = now;
        FrameCount++;
        InvalidateVisual();
        // The final empty draw clears the visual, then only the one-shot state timer remains.
        UpdateRendering();
    }

    private void Suspend()
    {
        _running = false;
        _scheduleTimer.Stop(); _frameTimer.Stop(); _visibleClock.Stop();
        if (Particles.HasParticles) { Particles.Clear(); InvalidateVisual(); }
    }

    private void StopSession()
    {
        Suspend();
        _sessionStarted = false;
        _visibleClock.Reset();
    }

    private Rect SceneBounds(FrameworkElement? element)
    {
        if (element is null || !element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0 || Parent is not Visual parent)
            return Rect.Empty;
        return element.TransformToVisual(parent).TransformBounds(new Rect(element.RenderSize));
    }

    private void UpdateSceneBounds()
    {
        if (!CanRun) return;
        var rule = SceneBounds(_rule);
        var footer = SceneBounds(_footer);
        var clipHeight = Math.Clamp(!rule.IsEmpty ? rule.Top : !footer.IsEmpty ? footer.Top : ActualHeight, 0, ActualHeight);
        var image = _background?.Source;
        var viewport = new WeatherViewport(ActualWidth, ActualHeight, clipHeight, image?.Width ?? ActualWidth, image?.Height ?? ActualHeight);
        if (viewport == Viewport && _sceneClip is not null) return;
        var resized = viewport != Viewport;
        Viewport = viewport;
        var clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, clipHeight));
        clip.Freeze();
        _sceneClip = clip;
        if (resized) Particles.Clear();
        InvalidateVisual();
        UpdateRendering();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (!_running || _sceneClip is null || !Particles.HasParticles) return;
        // All weather moves continuously behind the UI; only the page/footer boundary clips it.
        drawing.PushClip(_sceneClip);
        if (Sources is { Length: > 0 } sources)
        {
            for (var i = 0; i < Particles.Leaves.Length; i++)
            {
                ref var leaf = ref Particles.Leaves[i];
                if (!leaf.Active) continue;
                var matrix = Matrix.Identity;
                matrix.Rotate(leaf.Rotation);
                matrix.Translate(leaf.DrawX, leaf.Y);
                _leafTransforms[i].Matrix = matrix;
                drawing.PushOpacity(leaf.Opacity);
                drawing.PushTransform(_leafTransforms[i]);
                var source = sources[leaf.SourceIndex % sources.Length];
                var ratio = source.Width / source.Height;
                var width = ratio >= 1 ? leaf.Size : leaf.Size * ratio;
                var height = ratio >= 1 ? leaf.Size / ratio : leaf.Size;
                drawing.DrawImage(source, new Rect(-width / 2, -height / 2, width, height));
                drawing.Pop(); drawing.Pop();
            }
        }
        foreach (var drop in Particles.Drops)
        {
            if (!drop.Active) continue;
            drawing.PushOpacity(Math.Min(1, drop.Age / 0.15));
            drawing.DrawLine(_rainPen, new Point(drop.X, drop.Y),
                new Point(drop.X - Controller.Wind * 0.002 * drop.Length, drop.Y - drop.Length));
            drawing.Pop();
        }
        foreach (var ripple in Particles.Ripples)
        {
            if (!ripple.Active) continue;
            var radius = ripple.Radius;
            drawing.PushOpacity(ripple.Opacity);
            drawing.DrawEllipse(null, _ripplePen, new Point(ripple.X, ripple.Y), radius, radius * FocusWeatherParticles.RippleAspectRatio);
            drawing.Pop();
        }
        drawing.Pop();
    }
}
