using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FocusApp.Desktop.Views;

/// <summary>One transient, non-interactive native animation per owner; never executes a click or command.</summary>
public static class RippleEffect
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(RippleEffect), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.RegisterAttached(
        "Color", typeof(Color), typeof(RippleEffect), new PropertyMetadata(Color.FromRgb(0xA9, 0xAF, 0xB9)));
    public static readonly DependencyProperty PeakOpacityProperty = DependencyProperty.RegisterAttached(
        "PeakOpacity", typeof(double), typeof(RippleEffect), new PropertyMetadata(.16),
        value => value is double opacity && opacity >= 0 && opacity <= 1);
    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration", typeof(Duration), typeof(RippleEffect), new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(350))),
        value => value is Duration duration && duration.HasTimeSpan && duration.TimeSpan > TimeSpan.Zero);
    public static readonly DependencyProperty IsCircularProperty = DependencyProperty.RegisterAttached(
        "IsCircular", typeof(bool), typeof(RippleEffect), new PropertyMetadata(false));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(RippleEffect), new PropertyMetadata(new CornerRadius()));
    public static readonly DependencyProperty OverlayTargetProperty = DependencyProperty.RegisterAttached(
        "OverlayTarget", typeof(FrameworkElement), typeof(RippleEffect), new PropertyMetadata(null));
    private static readonly DependencyProperty ActiveStateProperty = DependencyProperty.RegisterAttached(
        "ActiveState", typeof(RippleState), typeof(RippleEffect), new PropertyMetadata(null));
    private static readonly MouseButtonEventHandler MouseHandler = MouseDown;

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetColor(DependencyObject element, Color value) => element.SetValue(ColorProperty, value);
    public static Color GetColor(DependencyObject element) => (Color)element.GetValue(ColorProperty);
    public static void SetPeakOpacity(DependencyObject element, double value) => element.SetValue(PeakOpacityProperty, value);
    public static double GetPeakOpacity(DependencyObject element) => (double)element.GetValue(PeakOpacityProperty);
    public static void SetDuration(DependencyObject element, Duration value) => element.SetValue(DurationProperty, value);
    public static Duration GetDuration(DependencyObject element) => (Duration)element.GetValue(DurationProperty);
    public static void SetIsCircular(DependencyObject element, bool value) => element.SetValue(IsCircularProperty, value);
    public static bool GetIsCircular(DependencyObject element) => (bool)element.GetValue(IsCircularProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetOverlayTarget(DependencyObject element, FrameworkElement? value) => element.SetValue(OverlayTargetProperty, value);
    public static FrameworkElement? GetOverlayTarget(DependencyObject element) => (FrameworkElement?)element.GetValue(OverlayTargetProperty);

    private static void EnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement owner) return;
        if ((bool)args.NewValue)
        {
            owner.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, MouseHandler, true);
            owner.Unloaded += OwnerUnloaded;
        }
        else
        {
            owner.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, MouseHandler);
            owner.Unloaded -= OwnerUnloaded;
            Cancel(owner);
        }
    }

    private static void MouseDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not FrameworkElement owner || args.ChangedButton != MouseButton.Left) return;
        // A nested opted-in control owns its own wave; routing must not create another one.
        for (var current = args.OriginalSource as DependencyObject; current is not null && current != owner;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (GetIsEnabled(current)) return;
        Play(owner, args.GetPosition(owner));
    }

    internal static bool Play(FrameworkElement owner, Point point)
    {
        if (!GetIsEnabled(owner) || !owner.IsEnabled || !owner.IsVisible ||
            owner.ActualWidth <= 0 || owner.ActualHeight <= 0 || !new Rect(owner.RenderSize).Contains(point)) return false;
        var surface = GetOverlayTarget(owner) ?? owner;
        var layer = AdornerLayer.GetAdornerLayer(surface);
        if (layer is null || !surface.IsVisible) return false;

        var offset = owner.TranslatePoint(new Point(), surface);
        var bounds = new Rect(offset, owner.RenderSize);
        var origin = owner.TranslatePoint(point, surface);
        var corners = GetCornerRadius(owner);
        if (corners == new CornerRadius() && owner is Border border) corners = border.CornerRadius;
        Geometry clip = GetIsCircular(owner)
            ? new EllipseGeometry(bounds)
            : RoundedRectangle(bounds, corners);
        if (!clip.FillContains(origin)) return false;

        // Adorners live above the visual tree, so retain the card and viewport clipping explicitly.
        ScrollViewer? viewport = null;
        for (var ancestor = VisualTreeHelper.GetParent(owner); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FrameworkElement element) continue;
            if (element is ScrollViewer viewer) viewport ??= viewer;
            Geometry? boundary = null;
            var position = element.TranslatePoint(new Point(), surface);
            if (element is Border { CornerRadius: var radius } && radius != new CornerRadius())
                boundary = RoundedRectangle(new Rect(position, element.RenderSize), radius);
            else if (element is ScrollContentPresenter || element.ClipToBounds)
                boundary = new RectangleGeometry(new Rect(position, element.RenderSize));
            if (boundary is not null) clip = new CombinedGeometry(GeometryCombineMode.Intersect, clip, boundary);
        }
        clip.Freeze();
        if (!clip.FillContains(origin)) return false;

        var radiusToCover = GetIsCircular(owner)
            ? (origin - new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)).Length + bounds.Width / 2
            : new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight }.Max(corner => (corner - origin).Length);
        Cancel(owner);
        var adorner = new RippleAdorner(surface, origin, radiusToCover, GetColor(owner), clip);
        var state = new RippleState(owner, surface, layer, adorner, viewport);
        owner.SetValue(ActiveStateProperty, state);
        surface.SizeChanged += state.SizeChanged;
        if (viewport is not null) viewport.ScrollChanged += state.ScrollChanged;
        layer.Add(adorner);
        adorner.Start(GetDuration(owner), GetPeakOpacity(owner), state.Completed);
        return true;
    }

    private static void OwnerUnloaded(object sender, RoutedEventArgs args) => Cancel((FrameworkElement)sender);
    private static void Cancel(FrameworkElement owner) => ((RippleState?)owner.GetValue(ActiveStateProperty))?.Dispose();

    private static Geometry RoundedRectangle(Rect bounds, CornerRadius corners)
    {
        var limit = Math.Min(bounds.Width, bounds.Height) / 2;
        var tl = Math.Clamp(corners.TopLeft, 0, limit); var tr = Math.Clamp(corners.TopRight, 0, limit);
        var br = Math.Clamp(corners.BottomRight, 0, limit); var bl = Math.Clamp(corners.BottomLeft, 0, limit);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(bounds.Left + tl, bounds.Top), true, true);
            drawing.LineTo(new Point(bounds.Right - tr, bounds.Top), true, false);
            Corner(new Point(bounds.Right, bounds.Top + tr), tr);
            drawing.LineTo(new Point(bounds.Right, bounds.Bottom - br), true, false);
            Corner(new Point(bounds.Right - br, bounds.Bottom), br);
            drawing.LineTo(new Point(bounds.Left + bl, bounds.Bottom), true, false);
            Corner(new Point(bounds.Left, bounds.Bottom - bl), bl);
            drawing.LineTo(new Point(bounds.Left, bounds.Top + tl), true, false);
            Corner(new Point(bounds.Left + tl, bounds.Top), tl);

            void Corner(Point end, double radius)
            {
                if (radius == 0) drawing.LineTo(end, true, false);
                else drawing.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
        }
        return geometry;
    }

    private sealed class RippleState(FrameworkElement owner, FrameworkElement surface, AdornerLayer layer, RippleAdorner adorner, ScrollViewer? viewport) : IDisposable
    {
        private bool _disposed;
        public void Completed(object? sender, EventArgs args) => Dispose();
        public void SizeChanged(object sender, SizeChangedEventArgs args) => Dispose();
        public void ScrollChanged(object sender, ScrollChangedEventArgs args) => Dispose();
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            surface.SizeChanged -= SizeChanged;
            if (viewport is not null) viewport.ScrollChanged -= ScrollChanged;
            adorner.Stop();
            layer.Remove(adorner);
            owner.ClearValue(ActiveStateProperty);
        }
    }
}

internal sealed class RippleAdorner : Adorner
{
    private readonly ContainerVisual _clippedSurface = new();
    private readonly DrawingVisual _wave = new();
    private readonly ScaleTransform _scale;
    internal Point Origin { get; }
    internal double MaximumRadius { get; }

    public RippleAdorner(FrameworkElement surface, Point origin, double radius, Color color, Geometry clip) : base(surface)
    {
        Origin = origin; MaximumRadius = radius;
        IsHitTestVisible = false; Focusable = false;
        // AdornerLayer manages Adorner.Clip during layout. Keep our boundary on an
        // unscaled child so it survives layout and never grows with the wave.
        _clippedSurface.Clip = clip;
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(color, 0));
        brush.GradientStops.Add(new GradientStop(color, .8));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        using (var drawing = _wave.RenderOpen()) drawing.DrawEllipse(brush, null, origin, radius, radius);
        _scale = new ScaleTransform(.02, .02, origin.X, origin.Y);
        _wave.Transform = _scale;
        _clippedSurface.Children.Add(_wave);
        AddVisualChild(_clippedSurface);
    }

    public void Start(Duration duration, double opacity, EventHandler completed)
    {
        var grow = new DoubleAnimation(.02, 1, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimation(opacity, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += completed;
        BeginAnimation(OpacityProperty, fade);
    }
    public void Stop()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BeginAnimation(OpacityProperty, null);
    }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _clippedSurface : throw new ArgumentOutOfRangeException(nameof(index));
    protected override Size MeasureOverride(Size constraint) => AdornedElement.RenderSize;
    protected override Size ArrangeOverride(Size finalSize) => finalSize;
}
