using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CalendarDayRecordView : UserControl
{
    private ScrollViewer? _scroll;
    private Window? _owner;
    private Window? _hoverOwner;
    private readonly DispatcherTimer _taskOpenTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _taskCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    public CalendarDayRecordView()
    {
        InitializeComponent();
        _taskOpenTimer.Tick += (_, _) =>
        {
            _taskOpenTimer.Stop();
            if (!IsVisible || DataContext is not CalendarDayRecordViewModel { HasTasks: true }) return;
            RecordHoverTip.IsOpen = false;
            TaskPopup.IsOpen = true;
        };
        _taskCloseTimer.Tick += (_, _) => CloseTaskPreview();
        RecordHoverTip.PlacementTarget = this;
        Loaded += (_, _) =>
        {
            _scroll = Ancestor<ScrollViewer>(this);
            if (_scroll is not null) _scroll.ScrollChanged += ScrollChanged;
        };
        Unloaded += (_, _) =>
        {
            ClosePreviews();
            if (_scroll is not null) _scroll.ScrollChanged -= ScrollChanged;
            _scroll = null;
        };
        DataContextChanged += (_, _) => ClosePreviews();
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClosePreviews(); };
    }

    public void CloseTaskPreview()
    {
        _taskOpenTimer.Stop();
        _taskCloseTimer.Stop();
        TaskPopup.IsOpen = false;
    }
    private void ClosePreviews() { CloseTaskPreview(); RecordHoverTip.IsOpen = false; }
    private void ScrollChanged(object sender, ScrollChangedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, _scroll) && (e.VerticalChange != 0 || e.HorizontalChange != 0)) ClosePreviews(); }

    private void RecordHoverTip_Opening(object sender, ToolTipEventArgs e)
    {
        if (TaskPopup.IsOpen || _taskOpenTimer.IsEnabled || DataContext is not CalendarDayRecordViewModel) { e.Handled = true; return; }
        RecordHoverTip.PlacementTarget = this;
        if (Window.GetWindow(this)?.Content is FrameworkElement root)
            RecordHoverContent.MaxWidth = Math.Min(PoptipChrome.MaximumWidth, Math.Max(1, root.ActualWidth - 16));
    }

    private CustomPopupPlacement[] RecordHoverTip_Placement(Size popupSize, Size targetSize, Point offset)
    {
        if (Window.GetWindow(this)?.Content is not FrameworkElement root)
        {
            RecordHoverContent.SetPlacement(false, PoptipChrome.PointerLeftForAnchor(popupSize.Width / 2, popupSize.Width));
            return [new(new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 2), PopupPrimaryAxis.Horizontal)];
        }
        // Popup callbacks use device pixels, while the owner and chrome use DIPs.
        var dpi = VisualTreeHelper.GetDpi(this);
        var logicalPopup = new Size(popupSize.Width / dpi.DpiScaleX, popupSize.Height / dpi.DpiScaleY);
        var row = new Rect(TranslatePoint(new Point(), root), RenderSize);
        var placement = CalendarRecordHoverPlacement.Calculate(row, logicalPopup, root.RenderSize);
        RecordHoverContent.SetPlacement(placement.PointerOnTop, placement.PointerLeft);
        return [new(new Point(placement.Offset.X * dpi.DpiScaleX, placement.Offset.Y * dpi.DpiScaleY), PopupPrimaryAxis.Horizontal)];
    }

    private void RecordHoverTip_Opened(object sender, RoutedEventArgs e)
    {
        _hoverOwner = Window.GetWindow(this);
        if (_hoverOwner is null) return;
        _hoverOwner.SizeChanged += HoverOwner_Changed;
        _hoverOwner.LocationChanged += HoverOwner_Changed;
        _hoverOwner.Deactivated += HoverOwner_Changed;
    }

    private void RecordHoverTip_Closed(object sender, RoutedEventArgs e)
    {
        if (_hoverOwner is null) return;
        _hoverOwner.SizeChanged -= HoverOwner_Changed;
        _hoverOwner.LocationChanged -= HoverOwner_Changed;
        _hoverOwner.Deactivated -= HoverOwner_Changed;
        _hoverOwner = null;
    }

    private void HoverOwner_Changed(object? sender, EventArgs e) => RecordHoverTip.IsOpen = false;

    private void TaskCountAnchor_MouseEnter(object sender, MouseEventArgs e)
    {
        _taskCloseTimer.Stop();
        if (!TaskPopup.IsOpen) _taskOpenTimer.Start();
    }

    private void TaskPreview_MouseEnter(object sender, MouseEventArgs e) => _taskCloseTimer.Stop();

    private void TaskPreview_MouseLeave(object sender, MouseEventArgs e)
    {
        _taskOpenTimer.Stop();
        _taskCloseTimer.Start();
    }

    private CustomPopupPlacement[] TaskPopup_Placement(Size popupSize, Size targetSize, Point offset)
        => [new(new Point(targetSize.Width - popupSize.Width + 4, -popupSize.Height - 2), PopupPrimaryAxis.Vertical),
            new(new Point(targetSize.Width - popupSize.Width + 4, targetSize.Height + 2), PopupPrimaryAxis.Vertical)];

    private void TaskPopup_Opened(object? sender, EventArgs e)
    {
        _owner = Window.GetWindow(this);
        if (_owner is not null)
        {
            _owner.PreviewKeyDown += Owner_KeyDown;
            _owner.SizeChanged += TaskOwner_Changed;
            _owner.LocationChanged += TaskOwner_Changed;
            _owner.Deactivated += TaskOwner_Changed;
        }
        TaskPoptipSurface.PointerOnTop = TaskPoptipSurface.PointToScreen(new Point()).Y >= TaskCountAnchor.PointToScreen(new Point()).Y;
        var anchor = TaskCountAnchor.PointToScreen(new Point(TaskCountAnchor.ActualWidth / 2, 0));
        var local = TaskPoptipSurface.PointFromScreen(anchor);
        TaskPoptipSurface.PointerLeft = PoptipChrome.PointerLeftForAnchor(local.X, TaskPoptipSurface.ActualWidth);
    }
    private void TaskPopup_Closed(object? sender, EventArgs e)
    {
        _taskOpenTimer.Stop();
        _taskCloseTimer.Stop();
        if (_owner is not null)
        {
            _owner.PreviewKeyDown -= Owner_KeyDown;
            _owner.SizeChanged -= TaskOwner_Changed;
            _owner.LocationChanged -= TaskOwner_Changed;
            _owner.Deactivated -= TaskOwner_Changed;
        }
        _owner = null;
    }
    private void TaskOwner_Changed(object? sender, EventArgs e) => CloseTaskPreview();
    private void Owner_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape) { CloseTaskPreview(); e.Handled = true; } }

    private static T? Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }
}
