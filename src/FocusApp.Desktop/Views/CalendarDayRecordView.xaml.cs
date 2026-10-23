using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CalendarDayRecordView : UserControl
{
    private ScrollViewer? _scroll;
    private Window? _owner;
    public CalendarDayRecordView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _scroll = Ancestor<ScrollViewer>(this);
            if (_scroll is not null) _scroll.ScrollChanged += ScrollChanged;
        };
        Unloaded += (_, _) =>
        {
            CloseTaskPreview();
            if (_scroll is not null) _scroll.ScrollChanged -= ScrollChanged;
            _scroll = null;
        };
        DataContextChanged += (_, _) => CloseTaskPreview();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseTaskPreview(); };
    }

    public void CloseTaskPreview() => TaskPopup.IsOpen = false;
    private void ScrollChanged(object sender, ScrollChangedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, _scroll) && (e.VerticalChange != 0 || e.HorizontalChange != 0)) CloseTaskPreview(); }

    private void TaskCountButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CalendarDayRecordViewModel { HasTasks: true } entry)
        {
            Ancestor<StatisticsPage>(this)?.CalendarRecordInteraction.Select(entry.Record);
            TaskPopup.IsOpen = !TaskPopup.IsOpen;
        }
        e.Handled = true;
    }

    private CustomPopupPlacement[] TaskPopup_Placement(Size popupSize, Size targetSize, Point offset)
        => [new(new Point(targetSize.Width - popupSize.Width + 4, targetSize.Height + 2), PopupPrimaryAxis.Vertical),
            new(new Point(targetSize.Width - popupSize.Width + 4, -popupSize.Height - 2), PopupPrimaryAxis.Vertical)];

    private void TaskPopup_Opened(object? sender, EventArgs e)
    {
        _owner = Window.GetWindow(this);
        if (_owner is not null) _owner.PreviewKeyDown += Owner_KeyDown;
        TaskPoptipSurface.PointerOnTop = TaskPoptipSurface.PointToScreen(new Point()).Y >= TaskCountButton.PointToScreen(new Point()).Y;
        var anchor = TaskCountButton.PointToScreen(new Point(TaskCountButton.ActualWidth / 2, 0));
        var local = TaskPoptipSurface.PointFromScreen(anchor);
        TaskPoptipSurface.PointerLeft = PoptipChrome.PointerLeftForAnchor(local.X, TaskPoptipSurface.ActualWidth);
    }
    private void TaskPopup_Closed(object? sender, EventArgs e)
    {
        if (_owner is not null) _owner.PreviewKeyDown -= Owner_KeyDown;
        _owner = null;
    }
    private void TaskPopup_MouseDownOutside(object sender, MouseButtonEventArgs e)
    {
        // Consume the anchor click that dismisses the captured Popup so it cannot immediately reopen.
        if (!new Rect(TaskCountButton.RenderSize).Contains(e.GetPosition(TaskCountButton))) return;
        CloseTaskPreview();
        e.Handled = true;
    }
    private void Owner_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { CloseTaskPreview(); e.Handled = true; } }

    private static T? Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }
}
