using System.Windows;
using System.Windows.Controls;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CalendarFocusTimelineToolTip : UserControl
{
    public CalendarFocusTimelineToolTip()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            PoptipLayout.DataContext = e.NewValue is FocusSessionRecordViewModel record
                ? new CalendarRecordPoptipViewModel(record) : null;
        };
    }

    public CalendarRecordPoptipViewModel? PoptipModel => PoptipLayout.DataContext as CalendarRecordPoptipViewModel;
    public static readonly DependencyProperty PointerLeftProperty = DependencyProperty.Register(
        nameof(PointerLeft), typeof(double), typeof(CalendarFocusTimelineToolTip), new PropertyMetadata(double.NaN));
    public double PointerLeft { get => (double)GetValue(PointerLeftProperty); set => SetValue(PointerLeftProperty, value); }
    public void FitAboveTimeline(double availableHeight)
    {
        MaxHeight = double.PositiveInfinity;
        Measure(new Size(PoptipChrome.MaximumWidth, double.PositiveInfinity));
        if (PoptipModel is { HasTasks: true } model)
        {
            // Fit complete rows above the axis, keeping the remaining count accurate.
            var fixedHeight = DesiredSize.Height - model.TaskViewportHeight - (model.HasMore ? 26 : 0);
            var capacity = CalendarRecordPoptipViewModel.VisibleRowLimit;
            while (capacity > 1 && fixedHeight + Math.Min(capacity, model.AllRows.Count) *
                CalendarRecordPoptipViewModel.TaskRowHeight + (model.AllRows.Count > capacity ? 26 : 0) > availableHeight)
                capacity--;
            model.SetVisibleRowCapacity(capacity);
        }
        MaxHeight = availableHeight;
        ClipToBounds = true;
    }
}
