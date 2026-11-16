using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

public sealed class FocusMeteorLayer : Panel
{
    private static readonly string[] Captions =
    [
        "每一点努力，都在闪闪发光。",
        "你的努力，终会成为自己的星光。",
        "慢慢来，星光不会辜负赶路的人。",
        "即使微小，也可以闪闪发光。",
        "不必追赶流星，走好自己的路。",
        "看，夜空悄悄送来了一个惊喜。",
        "看，有一颗流星为你而来。"
    ];

    internal const double CaptionProgress = 0.30;
    private const int RouteSegments = 48;
    private const double SpriteOrigin = 15;
    private const double ArtworkLength = 200;
    private const double CaptionLeft = 0.625;
    private const double CaptionRight = 0.956;
    private static readonly Brush Halo = CreateHalo();
    private static readonly Brush Core = CreateBrush(255, 248, 253, 255);
    private static readonly Brush ParticleBrush = CreateBrush(255, 185, 221, 255);
    private readonly TextBlock _caption = new()
    {
        FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./FocusApp.Desktop;component/Assets/font/#WenKai T1 Medium"),
        FontSize = 19,
        Foreground = CreateBrush(255, 199, 221, 255),
        TextWrapping = TextWrapping.NoWrap,
        TextAlignment = TextAlignment.Center,
        IsHitTestVisible = false
    };
    private readonly Artwork _artwork = new();
    private readonly Artwork _captionDecoration = new() { Height = 20 };
    private readonly StackPanel _captionGroup = new() { Opacity = 0 };
    private readonly Viewbox _captionViewport = new() { StretchDirection = StretchDirection.DownOnly };
    private readonly DispatcherTimer _motionTimer;
    private readonly Stopwatch _motionClock = new();
    private readonly MatrixTransform _position = new();
    private readonly ScaleTransform _tailScale = new(1, 1, SpriteOrigin, SpriteOrigin);
    private readonly Point[] _points = new Point[RouteSegments + 1];
    private readonly Vector[] _directions = new Vector[RouteSegments + 1];
    private Size _sceneSize;
    private double _decorationWidth;
    private TimeSpan _duration;
    private bool _paused, _hasRoute;

    public FocusMeteorLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        _artwork.Visual.Transform = _position;
        Children.Add(_artwork);
        _captionGroup.Children.Add(_caption);
        _captionGroup.Children.Add(_captionDecoration);
        _captionViewport.Child = _captionGroup;
        Children.Add(_captionViewport);
        // Yield to input, countdown and render work when the dispatcher is busy.
        _motionTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _motionTimer.Tick += MotionTick;
        Unloaded += (_, _) => Stop();
    }

    internal bool IsPlaying => _duration > TimeSpan.Zero;
    internal event EventHandler? PlaybackFinished;

    internal void Play(int seconds)
    {
        if (IsPlaying || seconds <= 0) return;

        _duration = TimeSpan.FromSeconds(seconds);
        _caption.Text = Captions[Random.Shared.Next(Captions.Length)];
        Visibility = Visibility.Visible;
        _motionTimer.Interval = TimeSpan.FromMilliseconds(
            RenderCapability.Tier == 0 || RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly
                ? 33 : 15);
        DrawMeteor();
        UpdateRoute();
    }

    internal void Stop()
    {
        if (!IsPlaying) return;

        _motionTimer.Stop();
        _motionClock.Reset();
        _duration = TimeSpan.Zero;
        _paused = false;
        _caption.Text = string.Empty;
        _captionGroup.Opacity = 0;
        _artwork.Visual.Opacity = 0;
        using (_artwork.Visual.RenderOpen()) { }
        using (_captionDecoration.Visual.RenderOpen()) { }
        _hasRoute = false;
        _decorationWidth = 0;
        Visibility = Visibility.Collapsed;
    }

    internal void SetPaused(bool paused)
    {
        if (!IsPlaying || _paused == paused) return;

        _paused = paused;
        if (paused)
        {
            _motionClock.Stop();
            _motionTimer.Stop();
        }
        else if (_hasRoute)
        {
            _motionClock.Start();
            _motionTimer.Start();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _artwork.Measure(availableSize);
        // Measure the caption with the same constraint on every layout pass.
        // Re-measuring it in ArrangeOverride causes Viewbox to invalidate layout.
        _captionViewport.Measure(new Size(availableSize.Width * (CaptionRight - CaptionLeft), double.PositiveInfinity));
        return default;
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (size != _sceneSize)
        {
            _sceneSize = size;
            if (IsPlaying) UpdateRoute();
        }

        _artwork.Arrange(new Rect(size));
        var right = size.Width * CaptionRight;
        var captionSize = _captionViewport.DesiredSize;
        var width = Math.Min(captionSize.Width, size.Width * (CaptionRight - CaptionLeft));
        _captionViewport.Arrange(new Rect(right - width, Math.Max(108, size.Height * 0.19), width, captionSize.Height));
        if (IsPlaying) DrawCaptionDecoration(_captionGroup.RenderSize.Width);
        return size;
    }

    internal static PathGeometry CreateRoute(Size size)
    {
        var start = new Point(size.Width * 0.96, size.Height * 0.159);
        var end = new Point(0, size.Height * 0.34);
        var curve = new BezierSegment(new Point(size.Width * 0.64, size.Height * 0.160),
            new Point(size.Width * 0.22, size.Height * 0.21), end, true);
        return new PathGeometry([new PathFigure(start, [curve], false)]);
    }

    private void UpdateRoute()
    {
        if (_sceneSize.Width <= 0 || _sceneSize.Height <= 0)
        {
            _motionTimer.Stop();
            _hasRoute = false;
            return;
        }

        var route = CreateRoute(_sceneSize);
        route.Freeze();
        for (var index = 0; index < _points.Length; index++)
        {
            route.GetPointAtFractionLength(index / (double)RouteSegments, out _points[index], out var tangent);
            _directions[index] = new Vector(-tangent.X, -tangent.Y);
        }
        var tailLength = Math.Clamp(_sceneSize.Width * 0.25, 80, 280);
        _tailScale.ScaleX = tailLength / ArtworkLength;
        _hasRoute = true;
        if (!_paused && !_motionTimer.IsEnabled)
        {
            _motionClock.Start();
            _motionTimer.Start();
        }
        UpdatePose(Math.Min(_motionClock.Elapsed.TotalSeconds, _duration.TotalSeconds));
    }

    private void DrawMeteor()
    {
        var origin = new Point(SpriteOrigin, SpriteOrigin);
        using var spriteContext = _artwork.Visual.RenderOpen();
        spriteContext.PushTransform(_tailScale);
        var ribbon = new StreamGeometry();
        using (var writer = ribbon.Open())
        {
            writer.BeginFigure(origin + new Vector(0, -0.9), true, true);
            var tip = origin + new Vector(ArtworkLength, 13);
            var control = origin + new Vector(ArtworkLength * 0.55, 1.5);
            writer.QuadraticBezierTo(control, tip, true, false);
            writer.QuadraticBezierTo(control + new Vector(0, 1.8), origin + new Vector(0, 0.9), true, false);
        }
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(245, 222, 246, 255), 0),
                new GradientStop(Color.FromArgb(116, 133, 193, 255), 0.30),
                new GradientStop(Color.FromArgb(0, 74, 134, 243), 1)
            ]
        };
        ribbon.Freeze();
        var pen = new Pen(brush, 0.4);
        pen.Freeze();
        spriteContext.DrawGeometry(brush, pen, ribbon);
        for (var index = 0; index < 6; index++)
        {
            var sinceHead = (index + 1d) / 7;
            var offset = (index % 2 == 0 ? 1 : -1) * (3 + index);
            var mote = origin + new Vector(ArtworkLength * sinceHead * 0.63, 9 * sinceHead * sinceHead + offset);
            var radius = 0.72 - sinceHead * 0.42;
            spriteContext.PushOpacity(0.46 - sinceHead * 0.3);
            spriteContext.DrawEllipse(ParticleBrush, null, mote, radius, radius);
            spriteContext.Pop();
        }
        spriteContext.Pop();
        spriteContext.DrawEllipse(Halo, null, origin, 13, 13);
        spriteContext.DrawEllipse(Core, null, origin, 1.8, 1.8);
    }

    private void MotionTick(object? sender, EventArgs args)
    {
        var elapsed = _motionClock.Elapsed.TotalSeconds;
        if (elapsed >= _duration.TotalSeconds)
        {
            Stop();
            PlaybackFinished?.Invoke(this, EventArgs.Empty);
            return;
        }
        UpdatePose(elapsed);
    }

    private void UpdatePose(double elapsed)
    {
        var total = _duration.TotalSeconds;
        var distance = elapsed / total * RouteSegments;
        var index = Math.Min((int)distance, RouteSegments - 1);
        var fraction = distance - index;
        var point = _points[index] + (_points[index + 1] - _points[index]) * fraction;
        var direction = _directions[index] + (_directions[index + 1] - _directions[index]) * fraction;
        direction.Normalize();
        var position = new Matrix(direction.X, direction.Y, -direction.Y, direction.X, 0, 0);
        var origin = position.Transform(new Point(SpriteOrigin, SpriteOrigin));
        position.OffsetX = point.X - origin.X;
        position.OffsetY = point.Y - origin.Y;
        _position.Matrix = position;
        var fadeOut = Smooth((total - elapsed) / Math.Min(5, total * 0.25));
        _artwork.Visual.Opacity = Smooth(elapsed / Math.Min(1.4, total * 0.1)) * fadeOut;
        _captionGroup.Opacity = 0.94 * Smooth((elapsed - total * CaptionProgress) / 0.5) * fadeOut;
    }

    private static double Smooth(double progress)
    {
        var fraction = Math.Clamp(progress, 0, 1);
        return fraction * fraction * (3 - 2 * fraction);
    }
    private void DrawCaptionDecoration(double width)
    {
        if (width <= 0 || width == _decorationWidth) return;
        _decorationWidth = width;
        var start = new Point(width * 0.21, 16);
        var star = new Point(width * 0.86, 10);
        var arc = new PathGeometry([new PathFigure(start,
            [new BezierSegment(new Point(width * 0.46, 5), new Point(width * 0.69, 1), star, true)], false)]);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0, 132, 184, 247), 0),
                new GradientStop(Color.FromArgb(165, 159, 206, 255), 0.73),
                new GradientStop(Color.FromArgb(78, 147, 197, 255), 1)
            ]
        };
        var spark = new StreamGeometry();
        using (var writer = spark.Open())
        {
            writer.BeginFigure(star + new Vector(0, -6), true, true);
            writer.QuadraticBezierTo(star, star + new Vector(5, 0), true, false);
            writer.QuadraticBezierTo(star, star + new Vector(0, 6), true, false);
            writer.QuadraticBezierTo(star, star + new Vector(-5, 0), true, false);
            writer.QuadraticBezierTo(star, star + new Vector(0, -6), true, false);
        }
        using var dc = _captionDecoration.Visual.RenderOpen();
        dc.DrawGeometry(null, new Pen(brush, 0.8), arc);
        dc.DrawEllipse(Halo, null, star, 9, 9);
        dc.DrawGeometry(Core, null, spark);
    }

    private static Brush CreateHalo()
    {
        var halo = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(240, 216, 241, 255), 0),
                new GradientStop(Color.FromArgb(131, 119, 192, 255), 0.28),
                new GradientStop(Color.FromArgb(36, 94, 162, 255), 0.62),
                new GradientStop(Color.FromArgb(0, 94, 162, 255), 1)
            ]
        };
        halo.Freeze();
        return halo;
    }

    private static Brush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private sealed class Artwork : FrameworkElement
    {
        internal DrawingVisual Visual { get; } = new();

        internal Artwork() => AddVisualChild(Visual);
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => index == 0 ? Visual : throw new ArgumentOutOfRangeException(nameof(index));
    }
}
