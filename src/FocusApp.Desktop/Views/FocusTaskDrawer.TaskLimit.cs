using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskDrawer
{
    private Window? _taskLimitOwner;
    private DispatcherOperation? _taskLimitOpenOperation;

    private void DrawerAddTaskButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (ViewModel?.IsRootTaskLimitReached == true) ViewModel.ShowRootTaskLimitHint();
    }

    private void SynchronizeTaskLimitHint()
    {
        _taskLimitOpenOperation?.Abort();
        if (ViewModel?.IsRootTaskLimitHintOpen != true || !IsLoaded || !IsVisible)
        {
            RootTaskLimitPopup.IsOpen = false;
            return;
        }
        // A Tab press may have just opened the drawer and resized its owner.
        _taskLimitOpenOperation = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || !IsVisible || ViewModel?.IsRootTaskLimitHintOpen != true) return;
            UpdateLayout();
            PrepareTaskLimitPlacement();
            RootTaskLimitPopup.IsOpen = true;
        }, DispatcherPriority.Loaded);
    }

    private FrameworkElement TaskLimitBounds => Window.GetWindow(this)?.Content as FrameworkElement ?? this;

    private void PrepareTaskLimitPlacement()
    {
        RootTaskLimitPopupRoot.Width = Math.Max(1, Math.Min(300, TaskLimitBounds.ActualWidth - 16));
        RootTaskLimitPopup.CustomPopupPlacementCallback = PlaceTaskLimitHint;
    }

    private CustomPopupPlacement[] PlaceTaskLimitHint(Size popupSize, Size targetSize, Point offset)
    {
        var bounds = TaskLimitBounds;
        var origin = DrawerAddTaskButton.TranslatePoint(new Point(), bounds);
        // WPF's native-popup placement callback uses device pixels, while the
        // owner, shadow margin and chrome pointer are measured in DIPs.
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(DrawerAddTaskButton);
        var anchor = new Point(origin.X * dpi.DpiScaleX, origin.Y * dpi.DpiScaleY);
        var center = anchor.X + targetSize.Width / 2;
        var x = Math.Clamp(center - popupSize.Width + 37 * dpi.DpiScaleX, 8 * dpi.DpiScaleX,
            Math.Max(8 * dpi.DpiScaleX, bounds.ActualWidth * dpi.DpiScaleX - popupSize.Width - 8 * dpi.DpiScaleX));
        var y = Math.Clamp(anchor.Y - popupSize.Height + 10 * dpi.DpiScaleY, 8 * dpi.DpiScaleY,
            Math.Max(8 * dpi.DpiScaleY, bounds.ActualHeight * dpi.DpiScaleY - popupSize.Height - 8 * dpi.DpiScaleY));
        // The chrome has a 10px shadow margin; its pointer points at the button center.
        RootTaskLimitSurface.PointerLeft = (center - x) / dpi.DpiScaleX - 10 - RootTaskLimitSurface.PointerSize.Width / 2;
        return [new CustomPopupPlacement(new Point(x - anchor.X, y - anchor.Y), PopupPrimaryAxis.None)];
    }

    private void RootTaskLimitPopup_Opened(object? sender, EventArgs e)
    {
        _taskLimitOwner = Window.GetWindow(this);
        if (_taskLimitOwner is null) return;
        _taskLimitOwner.Deactivated += TaskLimitOwner_Deactivated;
        _taskLimitOwner.LocationChanged += TaskLimitOwner_PositionChanged;
        _taskLimitOwner.SizeChanged += TaskLimitOwner_SizeChanged;
        _taskLimitOwner.PreviewMouseDown += TaskLimitOwner_MouseDown;
    }

    private void RootTaskLimitPopup_Closed(object? sender, EventArgs e)
    {
        if (_taskLimitOwner is not null)
        {
            _taskLimitOwner.Deactivated -= TaskLimitOwner_Deactivated;
            _taskLimitOwner.LocationChanged -= TaskLimitOwner_PositionChanged;
            _taskLimitOwner.SizeChanged -= TaskLimitOwner_SizeChanged;
            _taskLimitOwner.PreviewMouseDown -= TaskLimitOwner_MouseDown;
            _taskLimitOwner = null;
        }
        ViewModel?.CloseRootTaskLimitHint();
    }

    private void TaskLimitOwner_Deactivated(object? sender, EventArgs e) => ViewModel?.CloseRootTaskLimitHint();
    private void TaskLimitOwner_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (ReferenceEquals(source, RootTaskLimitPopupRoot) || ReferenceEquals(source, DrawerAddTaskButton)) return;
            source = source switch
            {
                ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
                System.Windows.Media.Visual => System.Windows.Media.VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source)
            };
        }
        ViewModel?.CloseRootTaskLimitHint();
    }
    private void TaskLimitOwner_PositionChanged(object? sender, EventArgs e) => RefreshTaskLimitPlacement();
    private void TaskLimitOwner_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshTaskLimitPlacement();
    private void RefreshTaskLimitPlacement()
    {
        if (!RootTaskLimitPopup.IsOpen) return;
        PrepareTaskLimitPlacement();
        RootTaskLimitPopup.HorizontalOffset = RootTaskLimitPopup.HorizontalOffset == 0 ? 0.01 : 0;
    }

    private void CloseTaskLimitPresentation()
    {
        _taskLimitOpenOperation?.Abort();
        RootTaskLimitPopup.IsOpen = false;
        ViewModel?.CloseRootTaskLimitHint();
    }
}
