using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage
{
    public CalendarRecordInteractionState CalendarRecordInteraction { get; } = new();
    private readonly HashSet<Border> _calendarRecordRows = [];
    private Window? _calendarInteractionOwner;

    private void InitializeCalendarInteraction()
    {
        CalendarDayTimeline.InteractionState = CalendarRecordInteraction;
        CalendarDayTimeline.RecordSelected += CalendarTimeline_RecordSelected;
        CalendarRecordInteraction.Changed += (_, _) =>
        {
            foreach (var row in _calendarRecordRows) UpdateCalendarRecordRow(row);
        };
        PreviewMouseDown += CalendarOutside_MouseDown;
        Loaded += (_, _) =>
        {
            var owner = Window.GetWindow(this);
            if (ReferenceEquals(owner, _calendarInteractionOwner)) return;
            DetachCalendarInteractionOwner();
            _calendarInteractionOwner = owner;
            if (owner is not null) owner.PreviewMouseDown += CalendarOutside_MouseDown;
        };
        Unloaded += (_, _) =>
        {
            CalendarRecordInteraction.Clear();
            DetachCalendarInteractionOwner();
        };
    }

    private void DetachCalendarInteractionOwner()
    {
        if (_calendarInteractionOwner is not null) _calendarInteractionOwner.PreviewMouseDown -= CalendarOutside_MouseDown;
        _calendarInteractionOwner = null;
    }

    private void CalendarRecordRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border row) return;
        _calendarRecordRows.Add(row);
        UpdateCalendarRecordRow(row);
    }

    private void CalendarRecordRow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border row) return;
        _calendarRecordRows.Remove(row);
        if (GetCalendarRecord(row.DataContext) is { } record) CalendarRecordInteraction.Leave(record);
    }

    private void CalendarRecordRow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (GetCalendarRecord(e.OldValue) is { } record) CalendarRecordInteraction.Leave(record);
        if (sender is Border row) UpdateCalendarRecordRow(row);
    }

    private void UpdateCalendarRecordRow(Border row)
    {
        CalendarRecordInteractionState.SetIsHovered(row, GetCalendarRecord(row.DataContext) is { } hovered &&
            (ReferenceEquals(hovered, CalendarRecordInteraction.HoveredRecord) ||
             ReferenceEquals(hovered, CalendarRecordInteraction.HoveredTimelineRecord)));
        CalendarRecordInteractionState.SetIsSelected(row, GetCalendarRecord(row.DataContext) is { } selected &&
            ReferenceEquals(selected, CalendarRecordInteraction.SelectedRecord));
    }

    private static FocusSessionRecordViewModel? GetCalendarRecord(object? data) => data switch
    {
        CalendarDayRecordViewModel entry => entry.Record,
        FocusSessionRecordViewModel record => record,
        _ => null
    };

    private void CalendarRecordRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement row || GetCalendarRecord(row.DataContext) is not { } record) return;
        CalendarDayTimeline.EndTimelinePreview();
        CalendarRecordInteraction.Hover(record);
    }

    private void CalendarRecordRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement row && GetCalendarRecord(row.DataContext) is { } record) CalendarRecordInteraction.Leave(record);
    }

    private void ActivateCalendarRecord(FrameworkElement row)
    {
        if (GetCalendarRecord(row.DataContext) is not { } record) return;
        CalendarDayTimeline.SelectRecord(record);
        row.Focus();
    }

    private void CalendarRecordRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement row) return;
        ActivateCalendarRecord(row);
        e.Handled = true;
    }

    private void CalendarRecordRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not FrameworkElement row) return;
        ActivateCalendarRecord(row);
        e.Handled = true;
    }

    private void CalendarTimeline_RecordSelected(object? sender, FocusSessionRecordViewModel record)
    {
        if (DataContext is StatisticsOverviewViewModel model)
        {
            var period = model.SelectedDayPeriods.FirstOrDefault(group => group.Entries.Any(entry => ReferenceEquals(entry.Record, record)));
            if (period is not null) period.IsExpanded = true;
            UpdateLayout();
        }
        var row = _calendarRecordRows.FirstOrDefault(item => ReferenceEquals(GetCalendarRecord(item.DataContext), record));
        if (row is null) return;
        row.BringIntoView(new Rect(0, 0, row.ActualWidth, Math.Min(row.ActualHeight, CalendarRecordsScrollViewer.ViewportHeight)));
    }
    private void CalendarOutside_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (CalendarDayTimeline.IsWithinRecordPoptip(source)) return;
        for (DependencyObject? element = source; element is not null;)
        {
            if (ReferenceEquals(element, CalendarDayTimeline) || element is Border row && _calendarRecordRows.Contains(row)) return;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        CalendarRecordInteraction.Clear();
    }

}
