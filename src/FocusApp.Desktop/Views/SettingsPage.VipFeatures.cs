using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class SettingsPage
{
    private FrameworkElement? _vipFeatureAnchor;
    private SettingsToggleItemViewModel? _vipFeatureItem;
    private Window? _vipFeatureOwner;
    private DispatcherTimer? _vipFeatureLeaveTimer;

    private void VipFeatureArea_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SettingsToggleItemViewModel { IsVipRestricted: true } item } anchor)
            OpenVipFeatureHint(anchor, item);
    }

    private void VipFeatureArea_MouseLeave(object sender, MouseEventArgs e) => QueueVipFeatureHintClose();

    private void VipFeatureArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsToggleItemViewModel { IsVipRestricted: true } item } anchor) return;
        OpenVipFeatureHint(anchor, item);
        e.Handled = true;
    }

    private void VipFeatureRow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || sender is not ToggleButton { DataContext: SettingsToggleItemViewModel { IsVipRestricted: true } item } row) return;
        if (row.Template.FindName("VipFeatureHitArea", row) is FrameworkElement anchor) OpenVipFeatureHint(anchor, item);
        e.Handled = true;
    }

    private void OpenVipFeatureHint(FrameworkElement anchor, SettingsToggleItemViewModel item)
    {
        _vipFeatureLeaveTimer?.Stop();
        if (!item.IsVipRestricted || !IsLoaded || !IsVisible) return;
        if (VipFeaturePopup.IsOpen && ReferenceEquals(anchor, _vipFeatureAnchor)) return;
        CloseVipFeatureHint();
        _vipFeatureAnchor = anchor;
        _vipFeatureItem = item;
        item.PropertyChanged += VipFeatureItem_PropertyChanged;
        VipFeaturePopupContent.DataContext = item;
        VipFeaturePopup.PlacementTarget = anchor;
        VipFeaturePopupContent.Width = Math.Max(1, Math.Min(300, VipFeatureBounds.ActualWidth - 16));
        VipFeaturePopup.CustomPopupPlacementCallback = PlaceVipFeatureHint;
        VipFeaturePopup.IsOpen = true;
    }

    private FrameworkElement VipFeatureBounds => Window.GetWindow(this)?.Content as FrameworkElement ?? this;

    private CustomPopupPlacement[] PlaceVipFeatureHint(Size popupSize, Size targetSize, Point offset)
    {
        if (_vipFeatureAnchor is not { } anchor) return [];
        var bounds = VipFeatureBounds;
        var origin = anchor.TranslatePoint(new Point(), bounds);
        var dpi = VisualTreeHelper.GetDpi(anchor);
        var left = origin.X * dpi.DpiScaleX;
        var top = origin.Y * dpi.DpiScaleY;
        var lockX = left + 4 * dpi.DpiScaleX;
        var x = Math.Clamp(lockX - popupSize.Width + 37 * dpi.DpiScaleX, 8 * dpi.DpiScaleX,
            Math.Max(8 * dpi.DpiScaleX, bounds.ActualWidth * dpi.DpiScaleX - popupSize.Width - 8 * dpi.DpiScaleX));
        var above = top + 12 * dpi.DpiScaleY >= popupSize.Height - 2 * dpi.DpiScaleY;
        var desiredY = above ? top + 22 * dpi.DpiScaleY - popupSize.Height : top + 18 * dpi.DpiScaleY;
        var y = Math.Clamp(desiredY, 8 * dpi.DpiScaleY,
            Math.Max(8 * dpi.DpiScaleY, bounds.ActualHeight * dpi.DpiScaleY - popupSize.Height - 8 * dpi.DpiScaleY));
        VipFeaturePopupContent.Chrome.PointerOnTop = !above;
        VipFeaturePopupContent.Chrome.PointerLeft = (lockX - x) / dpi.DpiScaleX - 10 - VipFeaturePopupContent.Chrome.PointerSize.Width / 2;
        return [new CustomPopupPlacement(new Point(x - left, y - top), PopupPrimaryAxis.None)];
    }

    private void VipFeatureItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsVipRestricted) && _vipFeatureItem?.IsVipRestricted != true)
            CloseVipFeatureHint();
    }

    private void QueueVipFeatureHintClose()
    {
        if (!VipFeaturePopup.IsOpen) return;
        if (_vipFeatureLeaveTimer is null)
        {
            _vipFeatureLeaveTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(180) };
            _vipFeatureLeaveTimer.Tick += (_, _) =>
            {
                _vipFeatureLeaveTimer.Stop();
                if (_vipFeatureAnchor?.IsMouseOver != true && !VipFeaturePopupContent.IsMouseOver) CloseVipFeatureHint();
            };
        }
        _vipFeatureLeaveTimer.Stop();
        _vipFeatureLeaveTimer.Start();
    }

    private void VipFeaturePopup_MouseEnter(object sender, MouseEventArgs e) => _vipFeatureLeaveTimer?.Stop();
    private void VipFeaturePopup_MouseLeave(object sender, MouseEventArgs e) => QueueVipFeatureHintClose();
    private void VipFeaturePopup_MembershipRequested(object? sender, EventArgs e)
    {
        CloseVipFeatureHint();
        if (DataContext is SettingsPageViewModel model) model.OpenMembershipCommand.Execute(null);
    }

    private void VipFeaturePopup_Opened(object? sender, EventArgs e)
    {
        _vipFeatureOwner = Window.GetWindow(this);
        if (_vipFeatureOwner is null) return;
        _vipFeatureOwner.Deactivated += VipFeatureOwner_Deactivated;
        _vipFeatureOwner.LocationChanged += VipFeatureOwner_PositionChanged;
        _vipFeatureOwner.SizeChanged += VipFeatureOwner_SizeChanged;
        _vipFeatureOwner.PreviewMouseDown += VipFeatureOwner_MouseDown;
        _vipFeatureOwner.PreviewKeyDown += VipFeatureOwner_KeyDown;
    }

    private void VipFeaturePopup_Closed(object? sender, EventArgs e) => DetachVipFeatureHint();

    private void CloseVipFeatureHint()
    {
        VipFeaturePopup.IsOpen = false;
        DetachVipFeatureHint();
    }

    private void DetachVipFeatureHint()
    {
        _vipFeatureLeaveTimer?.Stop();
        if (_vipFeatureItem is not null) _vipFeatureItem.PropertyChanged -= VipFeatureItem_PropertyChanged;
        _vipFeatureItem = null;
        _vipFeatureAnchor = null;
        VipFeaturePopupContent.DataContext = null;
        if (_vipFeatureOwner is null) return;
        _vipFeatureOwner.Deactivated -= VipFeatureOwner_Deactivated;
        _vipFeatureOwner.LocationChanged -= VipFeatureOwner_PositionChanged;
        _vipFeatureOwner.SizeChanged -= VipFeatureOwner_SizeChanged;
        _vipFeatureOwner.PreviewMouseDown -= VipFeatureOwner_MouseDown;
        _vipFeatureOwner.PreviewKeyDown -= VipFeatureOwner_KeyDown;
        _vipFeatureOwner = null;
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0) CloseVipFeatureHint();
    }
    private void VipFeatureOwner_Deactivated(object? sender, EventArgs e) => CloseVipFeatureHint();
    private void VipFeatureOwner_SizeChanged(object sender, SizeChangedEventArgs e) => CloseVipFeatureHint();
    private void VipFeatureOwner_PositionChanged(object? sender, EventArgs e) => CloseVipFeatureHint();
    private void VipFeatureOwner_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseVipFeatureHint();
        e.Handled = true;
    }
    private void VipFeatureOwner_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (ReferenceEquals(source, VipFeaturePopupContent) || ReferenceEquals(source, _vipFeatureAnchor)) return;
            source = source switch
            {
                ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
                Visual => VisualTreeHelper.GetParent(source),
                _ => LogicalTreeHelper.GetParent(source)
            };
        }
        CloseVipFeatureHint();
    }
}
