using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsPage
{
    public CalendarRecordInteractionState CalendarRecordInteraction { get; } = new();
    private readonly HashSet<Border> _calendarRecordRows = [];

    private void InitializeCalendarInteraction()
    {
        CalendarDayTimeline.InteractionState = CalendarRecordInteraction;
        CalendarDayTimeline.RecordNavigationRequested += CalendarTimeline_RecordNavigationRequested;
        CalendarRecordInteraction.Changed += (_, _) =>
        {
            foreach (var row in _calendarRecordRows) UpdateCalendarRecordRow(row);
        };
        Unloaded += (_, _) => CalendarRecordInteraction.Clear();
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

    private void CalendarTimeline_RecordNavigationRequested(object? sender, FocusSessionRecordViewModel record)
    {
        if (DataContext is not StatisticsOverviewViewModel model) return;
        var period = model.SelectedDayPeriods.FirstOrDefault(group => group.Entries.Any(entry => ReferenceEquals(entry.Record, record)));
        if (period is null) return;
        period.IsExpanded = true;
        UpdateLayout();
        var row = _calendarRecordRows.FirstOrDefault(item => ReferenceEquals(GetCalendarRecord(item.DataContext), record));
        row?.BringIntoView(new Rect(0, 0, row.ActualWidth, Math.Min(row.ActualHeight, CalendarRecordsScrollViewer.ViewportHeight)));
    }

    private void CalendarRecordRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement row && GetCalendarRecord(row.DataContext) is { } record) CalendarRecordInteraction.Leave(record);
    }

}
