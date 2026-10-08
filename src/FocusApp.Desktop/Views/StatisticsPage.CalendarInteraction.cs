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
        if (row.DataContext is FocusSessionRecordViewModel record) CalendarRecordInteraction.Leave(record);
    }

    private void CalendarRecordRow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FocusSessionRecordViewModel record) CalendarRecordInteraction.Leave(record);
        if (sender is Border row) UpdateCalendarRecordRow(row);
    }

    private void UpdateCalendarRecordRow(Border row)
    {
        CalendarRecordInteractionState.SetIsHovered(row, row.DataContext is FocusSessionRecordViewModel hovered &&
            (ReferenceEquals(hovered, CalendarRecordInteraction.HoveredRecord) ||
             ReferenceEquals(hovered, CalendarRecordInteraction.HoveredTimelineRecord)));
        CalendarRecordInteractionState.SetIsSelected(row, row.DataContext is FocusSessionRecordViewModel selected &&
            ReferenceEquals(selected, CalendarRecordInteraction.SelectedRecord));
    }

    private void CalendarRecordRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FocusSessionRecordViewModel record }) return;
        CalendarDayTimeline.EndTimelinePreview();
        CalendarRecordInteraction.Hover(record);
    }

    private void CalendarRecordRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FocusSessionRecordViewModel record }) CalendarRecordInteraction.Leave(record);
    }

    private void CalendarRecordRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FocusSessionRecordViewModel record } row) return;
        CalendarDayTimeline.SelectRecord(record);
        row.Focus();
        e.Handled = true;
    }

    private void CalendarRecordRow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not FrameworkElement { DataContext: FocusSessionRecordViewModel record } row) return;
        CalendarDayTimeline.SelectRecord(record);
        e.Handled = true;
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

    private void CalendarRecords_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0) CalendarDayTimeline.RepositionRecordPoptip();
    }
}
