using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>A proportional overview of individual sessions within the selected day.</summary>
public sealed class CalendarFocusTimeline : FrameworkElement
{
    public static readonly DependencyProperty TimelineProperty = DependencyProperty.Register(
        nameof(Timeline), typeof(CalendarFocusTimelineViewModel), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CalendarFocusTimeline)owner).OnTimelineChanged()));

    public static readonly DependencyProperty FullDayStyleProperty = DependencyProperty.Register(
        nameof(FullDayStyle), typeof(bool), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CalendarFocusTimeline)owner).CloseSessionToolTip()));
    public bool FullDayStyle { get => (bool)GetValue(FullDayStyleProperty); set => SetValue(FullDayStyleProperty, value); }

    public static readonly DependencyProperty GoalHistoryStyleProperty = DependencyProperty.Register(
        nameof(GoalHistoryStyle), typeof(bool), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, _) => ((CalendarFocusTimeline)owner).ResetSessionToolTip()));
    public bool GoalHistoryStyle { get => (bool)GetValue(GoalHistoryStyleProperty); set => SetValue(GoalHistoryStyleProperty, value); }
    private double AxisLeft => GoalHistoryStyle ? 4 : 0;
    private double AxisWidth => Math.Max(0, ActualWidth - AxisLeft * 2);

    private ToolTip? _sessionToolTip;
    private CalendarFocusTimelineSegment? _hoveredSegment;
    private CalendarRecordInteractionState? _interactionState;
    private CalendarRecordPoptip? _recordPoptip;

    public CalendarRecordInteractionState? InteractionState
    {
        get => _interactionState;
        set
        {
            if (ReferenceEquals(_interactionState, value)) return;
            if (_interactionState is not null) _interactionState.Changed -= InteractionState_Changed;
            _interactionState = value;
            if (_interactionState is not null) _interactionState.Changed += InteractionState_Changed;
            InvalidateVisual();
        }
    }

    private void InteractionState_Changed(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (FullDayStyle && !GoalHistoryStyle) _recordPoptip?.Synchronize();
    }

    internal void SelectRecord(FocusSessionRecordViewModel record)
    {
        _recordPoptip ??= new CalendarRecordPoptip(this);
        _recordPoptip.CancelPendingLeave();
        InteractionState?.ToggleSelection(record);
        _recordPoptip.Synchronize();
    }
    internal void EndTimelinePreview()
    {
        _hoveredSegment = null;
        _recordPoptip?.CancelPendingLeave();
        InteractionState?.HoverTimeline(null);
    }
    internal bool TryGetRecordBounds(FocusSessionRecordViewModel record, out Rect bounds)
    {
        var segment = Timeline?.Segments.FirstOrDefault(item => ReferenceEquals(item.Record, record));
        bounds = segment is null ? Rect.Empty : SegmentBounds(segment);
        return segment is not null;
    }
    internal void LeaveRecordPoptip(FocusSessionRecordViewModel record) => _recordPoptip?.Leave(record);
    internal bool IsWithinRecordPoptip(DependencyObject source) => _recordPoptip?.Contains(source) == true;
    internal void RepositionRecordPoptip() => _recordPoptip?.Reposition();

    private void OnTimelineChanged()
    {
        CloseSessionToolTip();
        _recordPoptip?.Reset();
        if (InteractionState is { } state &&
            (state.SelectedRecord is { } selected && Timeline?.Segments.Any(segment => ReferenceEquals(segment.Record, selected)) != true ||
             state.HoveredTimelineRecord is { } hovered && Timeline?.Segments.Any(segment => ReferenceEquals(segment.Record, hovered)) != true))
            state.Clear();
        _recordPoptip?.Synchronize();
        InvalidateVisual();
    }

    public CalendarFocusTimeline()
    {
        Unloaded += (_, _) => { CloseSessionToolTip(); _recordPoptip?.Reset(); InteractionState?.Clear(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { CloseSessionToolTip(); _recordPoptip?.Close(); InteractionState?.Clear(); } };
        SizeChanged += (_, _) => { CloseSessionToolTip(); _recordPoptip?.Reposition(); };
    }

    public CalendarFocusTimelineViewModel? Timeline
    {
        get => (CalendarFocusTimelineViewModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly DependencyProperty TrackHeightProperty = DependencyProperty.Register(
        nameof(TrackHeight), typeof(double), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double TrackHeight { get => (double)GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }
    public static readonly DependencyProperty TrackTopProperty = DependencyProperty.Register(
        nameof(TrackTop), typeof(double), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(26d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double TrackTop { get => (double)GetValue(TrackTopProperty); set => SetValue(TrackTopProperty, value); }

    public static readonly DependencyProperty TickLabelFontSizeProperty = DependencyProperty.Register(
        nameof(TickLabelFontSize), typeof(double), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double TickLabelFontSize { get => (double)GetValue(TickLabelFontSizeProperty); set => SetValue(TickLabelFontSizeProperty, value); }

    public static readonly DependencyProperty TickLabelFontWeightProperty = DependencyProperty.Register(
        nameof(TickLabelFontWeight), typeof(FontWeight), typeof(CalendarFocusTimeline),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsRender));
    public FontWeight TickLabelFontWeight { get => (FontWeight)GetValue(TickLabelFontWeightProperty); set => SetValue(TickLabelFontWeightProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        // Transparent drawing enables hit testing around narrow time blocks without widening them.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var startHour = Timeline?.StartHour ?? (FullDayStyle || GoalHistoryStyle ? 0 : 8);
        var typeface = new Typeface(TryFindResource("FontFamilyEnglish") as FontFamily ?? new FontFamily("Segoe UI"),
            FontStyles.Normal, TickLabelFontWeight, FontStretches.Normal);
        var labelBrush = TryFindResource("TextWeak") as Brush ?? Brushes.Gray;
        var trackBrush = FullDayStyle ? new SolidColorBrush(Color.FromRgb(246, 247, 249))
            : TryFindResource("SurfaceTertiary") as Brush ?? new SolidColorBrush(Color.FromRgb(239, 240, 243));
        var accentBrush = GoalHistoryStyle ? new SolidColorBrush(Color.FromRgb(255, 151, 57))
            : FullDayStyle ? new SolidColorBrush(Color.FromRgb(255, 186, 132))
            : TryFindResource("AccentPrimary") as Brush ?? Brushes.DarkOrange;
        var tickPen = new Pen(FullDayStyle ? new SolidColorBrush(Color.FromRgb(232, 235, 240)) : trackBrush, 1);
        var trackTop = TrackTop;
        var trackHeight = TrackHeight;
        if (GoalHistoryStyle)
            drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(233, 236, 242)), 1),
                new Point(AxisLeft, trackTop + trackHeight / 2), new Point(AxisLeft + AxisWidth, trackTop + trackHeight / 2));
        for (var hour = startHour; hour <= 24; hour += 4)
        {
            var x = AxisLeft + (hour - startHour) / (double)(24 - startHour) * AxisWidth;
            var label = new FormattedText(FullDayStyle && !GoalHistoryStyle ? $"{hour:00}" : $"{hour:00}:00", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, TickLabelFontSize, labelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, Math.Max(0, ActualWidth - label.Width)), 0));
            if (GoalHistoryStyle)
                drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(205, 210, 220)), null,
                    new Point(x, trackTop + trackHeight / 2), 2.3, 2.3);
            else drawingContext.DrawLine(tickPen, new Point(x, 19), new Point(x, trackTop));
        }
        if (!GoalHistoryStyle)
            drawingContext.DrawRoundedRectangle(trackBrush, null, new Rect(0, trackTop, ActualWidth, trackHeight), 3, 3);
        if (FullDayStyle && !GoalHistoryStyle)
        {
            // The middle third is barely darker; the 08/16 boundaries stay deliberately quiet.
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(241, 243, 246)), null,
                new Rect(ActualWidth / 3, trackTop, ActualWidth / 3, trackHeight));
        }
        if (Timeline is null) return;
        foreach (var segment in Timeline.Segments)
        {
            if (FullDayStyle && !GoalHistoryStyle && InteractionState is { } activeState &&
                (ReferenceEquals(segment.Record, activeState.HoveredTimelineRecord) || ReferenceEquals(segment.Record, activeState.SelectedRecord))) continue;
            // Never impose a minimum width: position and duration remain proportional to real timestamps.
            drawingContext.DrawRoundedRectangle(accentBrush, GoalHistoryStyle ? new Pen(Brushes.White, 0.6) : null,
                SegmentBounds(segment), GoalHistoryStyle ? 3 : 1.5, GoalHistoryStyle ? 3 : 1.5);
        }
        if (FullDayStyle && !GoalHistoryStyle && InteractionState is { } state)
        {
            foreach (var segment in Timeline.Segments.Where(segment =>
                ReferenceEquals(segment.Record, state.HoveredTimelineRecord) || ReferenceEquals(segment.Record, state.SelectedRecord)))
            {
                var selected = ReferenceEquals(segment.Record, state.SelectedRecord);
                var bounds = SegmentBounds(segment);
                var center = bounds.Left + bounds.Width / 2;
                var haloWidth = Math.Max(20, bounds.Width + 12);
                var halo = new Rect(center - haloWidth / 2, bounds.Top - 6, haloWidth, bounds.Height + 10);
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(selected ? Color.FromArgb(16, 255, 132, 31)
                    : Color.FromRgb(247, 247, 248)), null, halo, 6, 6);
                // The original duration width stays unchanged; the active mark gets only a subtle lift.
                var active = new Rect(center - bounds.Width * 1.12 / 2, bounds.Top - 2, bounds.Width * 1.12, bounds.Height + 2);
                drawingContext.DrawRoundedRectangle(new SolidColorBrush(selected ? Color.FromRgb(255, 128, 0) : Color.FromRgb(255, 158, 75)), null, active, 2, 2);
            }
        }
    }

    // Use a larger hit area, resolving neighboring/overlapping areas to the closest real block.
    private CalendarFocusTimelineSegment? FindSessionAt(Point position)
    {
        if ((!FullDayStyle && !GoalHistoryStyle) || Timeline is null || AxisWidth <= 0 ||
            position.Y < TrackTop - (FullDayStyle && !GoalHistoryStyle ? 7 : 0) ||
            position.Y > TrackTop + TrackHeight + (FullDayStyle && !GoalHistoryStyle ? 7 : 0)) return null;
        if (FullDayStyle && !GoalHistoryStyle)
        {
            return Timeline.Segments.Where(segment => SegmentHitBounds(segment).Contains(position))
                .OrderBy(segment => Math.Abs(position.X - (SegmentBounds(segment).Left + SegmentBounds(segment).Width / 2)))
                .FirstOrDefault();
        }
        return Timeline.Segments.LastOrDefault(segment =>
            SegmentBounds(segment).Contains(position));
    }

    private Rect SegmentHitBounds(CalendarFocusTimelineSegment segment)
    {
        var bounds = SegmentBounds(segment);
        var width = Math.Max(24, bounds.Width);
        return new Rect(bounds.Left + bounds.Width / 2 - width / 2, bounds.Top - 7, width, bounds.Height + 14);
    }

    private Rect SegmentBounds(CalendarFocusTimelineSegment segment) =>
        new(AxisLeft + segment.StartRatio * AxisWidth, TrackTop, segment.WidthRatio * AxisWidth, TrackHeight);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateSessionHover(e.GetPosition(this));
    }

    private void UpdateSessionHover(Point position)
    {
        var segment = FindSessionAt(position);
        Cursor = segment is null ? Cursors.Arrow : Cursors.Hand;
        if (ReferenceEquals(segment, _hoveredSegment)) return;
        if (FullDayStyle && !GoalHistoryStyle)
        {
            if (_hoveredSegment is { } old) LeaveRecordPoptip(old.Record);
            _hoveredSegment = segment;
            if (segment is not null)
            {
                _recordPoptip ??= new CalendarRecordPoptip(this);
                _recordPoptip.CancelPendingLeave();
                InteractionState?.HoverTimeline(segment.Record);
                _recordPoptip?.Synchronize();
            }
            return;
        }
        CloseSessionToolTip();
        if (segment is null) return;
        Cursor = Cursors.Hand;
        _hoveredSegment = segment;
        if (_sessionToolTip is null)
        {
            UserControl content = GoalHistoryStyle ? new GoalFocusTimelineToolTip() : new CalendarFocusTimelineToolTip();
            _sessionToolTip = new ToolTip
            {
                Content = content,
                Style = (Style)content.Resources["SessionToolTipStyle"],
                PlacementTarget = this,
                Placement = PlacementMode.Custom,
                StaysOpen = true,
                IsHitTestVisible = false,
                CustomPopupPlacementCallback = PlaceSessionToolTip
            };
        }
        ((FrameworkElement)_sessionToolTip.Content).DataContext = segment.Record;
        _sessionToolTip.PlacementRectangle = SegmentBounds(segment);
        _sessionToolTip.IsOpen = true;
    }

    private CustomPopupPlacement[] PlaceSessionToolTip(Size popupSize, Size targetSize, Point offset)
    {
        var x = targetSize.Width / 2 - popupSize.Width / 2;
        if (GoalHistoryStyle && _sessionToolTip?.Content is GoalFocusTimelineToolTip content)
        {
            var bounds = _sessionToolTip.PlacementRectangle;
            var center = bounds.Left + bounds.Width / 2;
            var popupLeft = Math.Clamp(center - popupSize.Width / 2, 0, Math.Max(0, ActualWidth - popupSize.Width));
            x = popupLeft - bounds.Left;
            content.PointerLeft = Math.Clamp(center - popupLeft - 15, 2, 208);
        }
        return [new CustomPopupPlacement(new Point(x, -popupSize.Height - 2), PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(x, targetSize.Height + 2), PopupPrimaryAxis.Horizontal)];
    }

    private void ResetSessionToolTip()
    {
        CloseSessionToolTip();
        _sessionToolTip = null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (FullDayStyle && !GoalHistoryStyle)
        {
            if (_hoveredSegment is { } segment) LeaveRecordPoptip(segment.Record);
            _hoveredSegment = null;
            Cursor = Cursors.Arrow;
            return;
        }
        CloseSessionToolTip();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!FullDayStyle || GoalHistoryStyle || InteractionState is null) return;
        HandleSessionClick(e.GetPosition(this));
        e.Handled = true;
    }

    private void HandleSessionClick(Point position)
    {
        if (!FullDayStyle || GoalHistoryStyle || InteractionState is null) return;
        var segment = FindSessionAt(position);
        if (segment is null) InteractionState.Clear();
    }

    private void CloseSessionToolTip()
    {
        if (_sessionToolTip is not null) _sessionToolTip.IsOpen = false;
        if (FullDayStyle && !GoalHistoryStyle && _hoveredSegment is { } segment) InteractionState?.LeaveTimeline(segment.Record);
        _hoveredSegment = null;
        Cursor = Cursors.Arrow;
    }
}
