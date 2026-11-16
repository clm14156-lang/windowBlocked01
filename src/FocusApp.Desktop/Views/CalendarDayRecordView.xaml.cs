using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CalendarDayRecordView : UserControl
{
    private ScrollViewer? _scroll;
    private Window? _hoverOwner;
    public CalendarDayRecordView()
    {
        InitializeComponent();
        RecordHoverTip.PlacementTarget = this;
        Loaded += (_, _) =>
        {
            _scroll = Ancestor<ScrollViewer>(this);
            if (_scroll is not null) _scroll.ScrollChanged += ScrollChanged;
        };
        Unloaded += (_, _) =>
        {
            CloseRecordHoverTip();
            if (_scroll is not null) _scroll.ScrollChanged -= ScrollChanged;
            _scroll = null;
        };
        DataContextChanged += (_, _) => CloseRecordHoverTip();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseRecordHoverTip(); };
    }

    private void CloseRecordHoverTip() => RecordHoverTip.IsOpen = false;

    private void ScrollChanged(object sender, ScrollChangedEventArgs e)
    { if (ReferenceEquals(e.OriginalSource, _scroll) && (e.VerticalChange != 0 || e.HorizontalChange != 0)) CloseRecordHoverTip(); }

    private void RecordHoverTip_Opening(object sender, ToolTipEventArgs e)
    {
        if (DataContext is not CalendarDayRecordViewModel) { e.Handled = true; return; }
        RecordHoverTip.PlacementTarget = this;
        if (Window.GetWindow(this)?.Content is FrameworkElement root)
            RecordHoverContent.MaxWidth = Math.Min(CalendarRecordHoverToolTip.MaximumWidth, Math.Max(1, root.ActualWidth - 16));
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

    private void HoverOwner_Changed(object? sender, EventArgs e) => CloseRecordHoverTip();

    private static T? Ancestor<T>(DependencyObject child) where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T found) return found;
        return null;
    }
}
