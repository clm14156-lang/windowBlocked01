using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>An interactive poptip in the owner window, sharing the timeline/row selection.</summary>
internal sealed class CalendarRecordPoptip
{
    private readonly CalendarFocusTimeline _timeline;
    private readonly DispatcherTimer _leaveTimer;
    private FocusSessionRecordViewModel? _leavingRecord;
    private AdornerLayer? _layer;
    private PoptipAdorner? _adorner;
    private Window? _owner;

    public CalendarRecordPoptip(CalendarFocusTimeline timeline)
    {
        _timeline = timeline;
        Content = new CalendarFocusTimelineToolTip();
        _leaveTimer = new DispatcherTimer(DispatcherPriority.Input, timeline.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(160) };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (_leavingRecord is { } record) _timeline.InteractionState?.LeaveTimeline(record);
            _leavingRecord = null;
        };
        Content.MouseEnter += (_, _) => _leaveTimer.Stop();
        Content.MouseLeave += (_, _) =>
        {
            if (IsOpen && Content.DataContext is FocusSessionRecordViewModel record) Leave(record);
        };
        // Keep controls functional while containing their remaining bubbling events.
        Content.MouseDown += (_, e) => e.Handled = true;
        Content.MouseUp += (_, e) => e.Handled = true;
        Content.MouseWheel += (_, e) => e.Handled = true;
        Content.SizeChanged += (_, _) => Reposition();
    }

    public CalendarFocusTimelineToolTip Content { get; }
    public bool IsOpen { get; private set; }

    public void CancelPendingLeave() { _leaveTimer.Stop(); _leavingRecord = null; }

    public void Leave(FocusSessionRecordViewModel record)
    {
        _leavingRecord = record;
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    public void Synchronize()
    {
        var record = _timeline.InteractionState?.SelectedRecord ?? _timeline.InteractionState?.HoveredTimelineRecord;
        if (record is null || !_timeline.TryGetRecordBounds(record, out _)) { Close(); return; }
        Content.DataContext = record;
        IsOpen = true;
        if (_adorner is null)
        {
            _owner = Window.GetWindow(_timeline);
            var root = _owner?.Content as FrameworkElement ?? _timeline;
            _layer = AdornerLayer.GetAdornerLayer(root);
            if (_layer is not null)
            {
                _adorner = new PoptipAdorner(root, Content);
                _layer.Add(_adorner);
            }
            if (_owner is not null)
            {
                _owner.Deactivated += Owner_Deactivated;
                _owner.SizeChanged += Owner_SizeChanged;
                _owner.PreviewKeyDown += Owner_KeyDown;
            }
        }
        Reposition();
    }

    public bool Contains(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null;)
        {
            if (ReferenceEquals(current, Content)) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    public void Reposition()
    {
        if (_adorner is null || Content.DataContext is not FocusSessionRecordViewModel record ||
            !_timeline.TryGetRecordBounds(record, out var bounds)) return;
        var root = (FrameworkElement)_adorner.AdornedElement;
        Content.Measure(new Size(200, double.PositiveInfinity));
        var size = Content.DesiredSize;
        var point = _timeline.TranslatePoint(bounds.TopLeft, root);
        var center = point.X + bounds.Width / 2;
        var x = Math.Clamp(center - size.Width / 2, 0, Math.Max(0, root.ActualWidth - size.Width));
        // The timeline is the only anchor. Never flip underneath it or toward a row.
        var availableHeight = Math.Max(0, point.Y + 2);
        Content.FitAboveTimeline(availableHeight);
        Content.Measure(new Size(200, availableHeight));
        size = Content.DesiredSize;
        var y = Math.Max(0, point.Y - size.Height + 2);
        Content.PointerLeft = Math.Clamp(center - x - 7, 5, Math.Max(5, size.Width - 19));
        _adorner.Position = new(x, y);
        _adorner.InvalidateMeasure();
    }

    public void Close()
    {
        _leaveTimer.Stop();
        IsOpen = false;
        if (_adorner is not null) { _layer?.Remove(_adorner); _adorner.Detach(); }
        _adorner = null;
        _layer = null;
        if (_owner is not null)
        {
            _owner.Deactivated -= Owner_Deactivated;
            _owner.SizeChanged -= Owner_SizeChanged;
            _owner.PreviewKeyDown -= Owner_KeyDown;
        }
        _owner = null;
        Content.DataContext = null;
    }

    public void Reset() => Close();
    private void Owner_Deactivated(object? sender, EventArgs e) => _timeline.InteractionState?.Clear();
    private void Owner_SizeChanged(object sender, SizeChangedEventArgs e) => Reposition();
    private void Owner_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _timeline.InteractionState?.Clear();
        e.Handled = true;
    }

    private sealed class PoptipAdorner : Adorner
    {
        private readonly FrameworkElement _content;
        private readonly VisualCollection _visuals;
        public PoptipAdorner(UIElement root, FrameworkElement content) : base(root)
        {
            _content = content;
            _visuals = new VisualCollection(this) { content };
            AddLogicalChild(content);
        }
        public Point Position { get; set; }
        public void Detach() { RemoveLogicalChild(_content); _visuals.Remove(_content); }
        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];
        protected override System.Collections.IEnumerator LogicalChildren
            => (_visuals.Count > 0 ? new[] { _content } : []).GetEnumerator();
        protected override Size MeasureOverride(Size available)
        {
            _content.Measure(new Size(200, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }
        protected override Size ArrangeOverride(Size size)
        {
            _content.Arrange(new Rect(Position, _content.DesiredSize));
            return size;
        }
    }
}
