using System.Windows;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

/// <summary>View-only state shared by the calendar timeline and its record rows.</summary>
public sealed class CalendarRecordInteractionState
{
    public static readonly DependencyProperty IsHoveredProperty = DependencyProperty.RegisterAttached(
        "IsHovered", typeof(bool), typeof(CalendarRecordInteractionState), new PropertyMetadata(false));
    public static bool GetIsHovered(DependencyObject element) => (bool)element.GetValue(IsHoveredProperty);
    public static void SetIsHovered(DependencyObject element, bool value) => element.SetValue(IsHoveredProperty, value);

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(CalendarRecordInteractionState), new PropertyMetadata(false));
    public static bool GetIsSelected(DependencyObject element) => (bool)element.GetValue(IsSelectedProperty);
    public static void SetIsSelected(DependencyObject element, bool value) => element.SetValue(IsSelectedProperty, value);

    public FocusSessionRecordViewModel? HoveredRecord { get; private set; }
    public FocusSessionRecordViewModel? HoveredTimelineRecord { get; private set; }
    public FocusSessionRecordViewModel? SelectedRecord { get; private set; }
    public event EventHandler? Changed;

    public void Hover(FocusSessionRecordViewModel? record)
    {
        if (ReferenceEquals(HoveredRecord, record)) return;
        HoveredRecord = record;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Leave(FocusSessionRecordViewModel record)
    {
        if (ReferenceEquals(HoveredRecord, record)) Hover(null);
    }

    public void ToggleSelection(FocusSessionRecordViewModel record)
    {
        if (ReferenceEquals(SelectedRecord, record))
        {
            // Consume the second click as deselection, including any stale hover preview.
            Clear();
            return;
        }
        SelectedRecord = record;
        HoveredTimelineRecord = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void HoverTimeline(FocusSessionRecordViewModel? record)
    {
        if (ReferenceEquals(HoveredTimelineRecord, record)) return;
        HoveredTimelineRecord = record;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void LeaveTimeline(FocusSessionRecordViewModel record)
    {
        if (ReferenceEquals(HoveredTimelineRecord, record)) HoverTimeline(null);
    }

    public void ClearSelection()
    {
        if (SelectedRecord is null) return;
        SelectedRecord = null;
        HoveredTimelineRecord = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        HoveredRecord = null;
        HoveredTimelineRecord = null;
        SelectedRecord = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
