using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public sealed class FocusDistributionChart : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(StatisticsDashboardViewModel),
        typeof(FocusDistributionChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ModelChanged));
    private static readonly DependencyProperty ActivationProperty = DependencyProperty.Register("Activation", typeof(double), typeof(FocusDistributionChart),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Brush EmptyBrush = OverviewFocusChart.FrozenBrush("#EEF2F7");
    private static readonly Brush ShadowBrush = OverviewFocusChart.FrozenBrush("#120F2341");
    private static readonly Pen SoftShadow = FrozenPen("#070F2341", 6);
    private static readonly Pen Separator = FrozenPen("#BBFFFFFF", .7);
    private readonly List<Sector> _sectors = [];
    private StatisticsDashboardViewModel? _subscribedModel;
    private FocusDistributionItemViewModel? _active;
    private double _activationTarget;
    private FocusDistributionItemViewModel? _pointerItem;
    private Size _geometrySize;
    private IReadOnlyList<FocusDistributionItemViewModel>? _geometryItems;
    public StatisticsDashboardViewModel? Model { get => (StatisticsDashboardViewModel?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    internal double Activation => (double)GetValue(ActivationProperty);
    public event EventHandler? InteractionChanged;

    public FocusDistributionChart()
    {
        Cursor = Cursors.Hand;
        Loaded += (_, _) => SubscribeModel();
        Unloaded += (_, _) => { UnsubscribeModel(); BeginAnimation(ActivationProperty, null); _pointerItem = null; };
    }
    private static void ModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (FocusDistributionChart)d;
        chart.UnsubscribeModel();
        if (chart.IsLoaded) chart.SubscribeModel();
        chart._geometryItems = null;
        chart.UpdateActivation();
    }
    private void SubscribeModel()
    {
        if (ReferenceEquals(_subscribedModel, Model)) return;
        _subscribedModel = Model;
        if (_subscribedModel is not null) _subscribedModel.PropertyChanged += ModelPropertyChanged;
        UpdateActivation();
    }
    private void UnsubscribeModel()
    {
        if (_subscribedModel is not null) _subscribedModel.PropertyChanged -= ModelPropertyChanged;
        _subscribedModel = null;
    }
    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)) { _pointerItem = null; _geometryItems = null; }
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(StatisticsDashboardViewModel.ActiveDistribution)) UpdateActivation();
    }
    private void UpdateActivation()
    {
        var active = Model?.ActiveDistribution;
        var target = active is null ? 0d : 1d;
        var sameTarget = active is not null && active.TargetId == _active?.TargetId;
        if (sameTarget) _active = active;
        if (!ReferenceEquals(active, _active) || _activationTarget != target)
        {
            var previous = _active;
            var rendered = active ?? previous;
            _active = rendered;
            var from = sameTarget || previous is not null && active is null ? Activation : 0;
            var to = target;
            _activationTarget = target;
            // The base geometries stay fixed for hit testing throughout the animation.
            if (!SystemParameters.ClientAreaAnimation || !IsVisible)
            {
                BeginAnimation(ActivationProperty, null);
                SetValue(ActivationProperty, to);
                _active = active;
            }
            else
            {
                var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(140))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                if (active is null) animation.Completed += (_, _) =>
                {
                    if (Model?.ActiveDistribution is null && ReferenceEquals(_active, rendered)) { _active = null; InvalidateVisual(); }
                };
                BeginAnimation(ActivationProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }
        }
        InvalidateVisual();
    }
    private Point Center => new(ActualWidth / 2, ActualHeight / 2);
    private double Radius => Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 12);
    private void EnsureGeometry()
    {
        if (_geometrySize == RenderSize && ReferenceEquals(_geometryItems, Model?.Distribution)) return;
        _geometrySize = RenderSize;
        _geometryItems = Model?.Distribution;
        _sectors.Clear();
        var start = -90d;
        foreach (var item in Model?.Distribution ?? [])
        {
            var sweep = item.Ratio * 360;
            _sectors.Add(new(item, RingSector(Center, Radius, Radius * .59, start, sweep), start + sweep / 2));
            start += sweep;
        }
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        EnsureGeometry();
        if (Radius <= 0) return;
        if (_sectors.Count == 0)
        {
            drawing.DrawGeometry(EmptyBrush, null, RingSector(Center, Radius, Radius * .59, 0, 360));
            return;
        }
        foreach (var sector in _sectors.Where(sector => !ReferenceEquals(sector.Item, _active)))
            drawing.DrawGeometry(sector.Item.Color, Separator, sector.Geometry);
        if (_sectors.FirstOrDefault(sector => ReferenceEquals(sector.Item, _active)) is { } selected)
        {
            var transform = SectorTransform(selected);
            drawing.PushTransform(transform);
            drawing.PushOpacity(Activation);
            drawing.PushTransform(new TranslateTransform(0, 2));
            drawing.DrawGeometry(ShadowBrush, SoftShadow, selected.Geometry);
            drawing.Pop();
            drawing.Pop();
            drawing.DrawGeometry(selected.Item.Color, Separator, selected.Geometry);
            drawing.Pop();
        }
    }
    private MatrixTransform SectorTransform(Sector sector)
    {
        var matrix = Matrix.Identity;
        matrix.ScaleAt(1 + .022 * Activation, 1 + .022 * Activation, Center.X, Center.Y);
        var radians = sector.MidAngle * Math.PI / 180;
        matrix.Translate(Math.Cos(radians) * 4 * Activation, Math.Sin(radians) * 4 * Activation);
        return new MatrixTransform(matrix);
    }
    internal FocusDistributionItemViewModel? HitTestItem(Point point)
    {
        EnsureGeometry();
        foreach (var sector in _sectors)
        {
            if (sector.Geometry.FillContains(point)) return sector.Item;
            if (ReferenceEquals(sector.Item, _active) && SectorTransform(sector).Inverse is { } inverse && sector.Geometry.FillContains(inverse.Transform(point))) return sector.Item;
        }
        return null;
    }
    internal Point AnchorFor(FocusDistributionItemViewModel item)
    {
        EnsureGeometry();
        var angle = (_sectors.FirstOrDefault(sector => ReferenceEquals(sector.Item, item))?.MidAngle ?? -90) * Math.PI / 180;
        return new Point(Center.X + Math.Cos(angle) * Radius * .82, Center.Y + Math.Sin(angle) * Radius * .82);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        HoverAt(e.GetPosition(this));
    }
    internal void HoverAt(Point point)
    {
        var item = HitTestItem(point);
        if (ReferenceEquals(item, _pointerItem)) return;
        _pointerItem = item;
        Model?.HoverDistribution(item);
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pointerItem = null;
        Model?.HoverDistribution(null);
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (SelectAt(e.GetPosition(this))) e.Handled = true;
    }
    internal bool SelectAt(Point point)
    {
        var item = HitTestItem(point);
        if (item is null) return false;
        Model?.ToggleDistribution(item);
        InteractionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    internal static Geometry RingSector(Point center, double outer, double inner, double start, double sweep)
    {
        if (sweep >= 359.999999)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(center, outer, outer));
            ring.Children.Add(new EllipseGeometry(center, inner, inner));
            ring.Freeze();
            return ring;
        }
        Point At(double angle, double radius) => new(center.X + Math.Cos(angle * Math.PI / 180) * radius, center.Y + Math.Sin(angle * Math.PI / 180) * radius);
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            c.BeginFigure(At(start, outer), true, true);
            c.ArcTo(At(start + sweep, outer), new Size(outer, outer), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            c.LineTo(At(start + sweep, inner), true, false);
            c.ArcTo(At(start, inner), new Size(inner, inner), 0, sweep > 180, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
    private sealed record Sector(FocusDistributionItemViewModel Item, Geometry Geometry, double MidAngle);
    private static Pen FrozenPen(string color, double width)
    {
        var pen = new Pen(OverviewFocusChart.FrozenBrush(color), width);
        pen.Freeze();
        return pen;
    }
}
