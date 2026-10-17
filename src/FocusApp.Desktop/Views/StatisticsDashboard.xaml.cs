using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class StatisticsDashboard : UserControl
{
    private StatisticsDashboardViewModel? _model;
    private FrameworkElement? _hoveredRow;
    private HwndSource? _tipSource;
    private DispatcherOperation? _tipRefreshOperation;
    private readonly DispatcherTimer _dateRefreshTimer;
    public StatisticsDashboard()
    {
        InitializeComponent();
        _dateRefreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _dateRefreshTimer.Tick += (_, _) => _model?.RefreshCurrentDate();
        DataContextChanged += (_, _) => AttachModel();
        Loaded += (_, _) => { AttachModel(); if (IsVisible) _dateRefreshTimer.Start(); };
        Unloaded += (_, _) => { _dateRefreshTimer.Stop(); ClosePeriodPicker(); CloseTip(); _model?.ClearDistributionSelection(); DetachModel(); };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _dateRefreshTimer.Stop(); ClosePeriodPicker(); CloseTip(); _model?.ClearDistributionSelection(); FocusChart.ClearHover(); }
            else if (IsLoaded) { _model?.RefreshCurrentDate(); _dateRefreshTimer.Start(); }
        };
        FocusChart.HoverChanged += FocusChart_HoverChanged;
        DistributionChart.InteractionChanged += (_, _) => { _hoveredRow = null; UpdateDistributionTip(); };
        SizeChanged += (_, _) => { ClosePeriodPicker(); CloseTip(); };
    }
    private void AttachModel()
    {
        ClosePeriodPicker();
        DetachModel();
        _model = IsLoaded ? DataContext as StatisticsDashboardViewModel : null;
        if (_model is not null) _model.PropertyChanged += Model_PropertyChanged;
        _model?.RefreshCurrentDate();
        CloseTip();
    }
    private void ClosePeriodPicker() { if (_model is not null) _model.IsPeriodPickerOpen = false; }
    private void DetachModel()
    {
        if (_model is not null) _model.PropertyChanged -= Model_PropertyChanged;
        _model = null;
    }
    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            _hoveredRow = null;
            CloseTip();
            if (_model?.SelectedDistribution is not null)
            {
                var model = _model;
                _tipRefreshOperation = Dispatcher.InvokeAsync(() =>
                {
                    _tipRefreshOperation = null;
                    if (ReferenceEquals(model, _model)) UpdateDistributionTip();
                }, DispatcherPriority.Loaded);
            }
        }
        else if (e.PropertyName == nameof(StatisticsDashboardViewModel.ActiveDistribution)) UpdateDistributionTip();
        else if (e.PropertyName == nameof(StatisticsDashboardViewModel.IsPeriodPickerOpen) && _model?.IsPeriodPickerOpen == true) CloseTip();
    }
    private void FocusChart_HoverChanged(object? sender, EventArgs e)
    {
        if (FocusChart.HoveredBar is not { } bar) { CloseTip(); return; }
        TipTitle.Text = bar.TooltipDate;
        TipDuration.Text = bar.DurationDisplay;
        TipPercentage.Visibility = Visibility.Collapsed;
        ShowTip(FocusChart.TranslatePoint(FocusChart.HoverAnchor, DashboardRoot));
    }
    private void DistributionRow_MouseEnter(object sender, MouseEventArgs e) => ActivateRow(sender);
    private void DistributionRow_MouseLeave(object sender, MouseEventArgs e) => DeactivateRow(sender);
    private void DeactivateRow(object sender)
    {
        if (ReferenceEquals(sender, _hoveredRow)) { _hoveredRow = null; _model?.HoverDistribution(null); UpdateDistributionTip(); }
    }
    private void DistributionRow_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ActivateRow(sender);
    private void DistributionRow_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { IsMouseOver: false }) DeactivateRow(sender);
    }
    private void ActivateRow(object sender)
    {
        if (sender is not FrameworkElement { DataContext: FocusDistributionItemViewModel item } row) return;
        _hoveredRow = row;
        _model?.HoverDistribution(item);
        UpdateDistributionTip();
    }
    private void DistributionRow_Click(object sender, RoutedEventArgs e)
    {
        // Button executes its bound command after Click is routed. Let the model's
        // resulting ActiveDistribution notification update the shared poptip.
        e.Handled = true;
    }
    private void UpdateDistributionTip()
    {
        if (_model?.ActiveDistribution is not { } item || !IsVisible || FocusChart.IsMouseOver && FocusChart.HoveredBar is null) { CloseTip(); return; }
        TipTitle.Text = item.TargetName;
        TipDuration.Text = item.ExactDurationDisplay;
        TipPercentage.Text = $"占比  {item.PercentageDisplay}";
        TipPercentage.Visibility = Visibility.Visible;
        var point = _hoveredRow is { IsVisible: true } row && ReferenceEquals(row.DataContext, item)
            ? row.TranslatePoint(new Point(row.ActualWidth / 2, 0), DashboardRoot)
            : DistributionChart.TranslatePoint(DistributionChart.AnchorFor(item), DashboardRoot);
        ShowTip(point);
    }
    private void ShowTip(Point anchor)
    {
        if (!IsLoaded || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 || _model?.IsPeriodPickerOpen == true) return;
        // Inline text changes can leave the popup's previous desired width cached until layout runs.
        OverviewTipSurface.InvalidateMeasure();
        OverviewTipSurface.UpdateLayout();
        OverviewTipSurface.Measure(new Size(240, double.PositiveInfinity));
        var size = OverviewTipSurface.DesiredSize;
        var x = Math.Clamp(anchor.X - size.Width / 2, 2, Math.Max(2, ActualWidth - size.Width - 2));
        var y = anchor.Y - size.Height - 8;
        if (y < 2) y = anchor.Y + 12;
        y = Math.Clamp(y, 2, Math.Max(2, ActualHeight - size.Height - 2));
        OverviewTipSurface.PointerOnTop = y >= anchor.Y;
        OverviewTipSurface.PointerLeft = PoptipChrome.PointerLeftForAnchor(anchor.X - x, size.Width);
        OverviewTip.HorizontalOffset = x;
        OverviewTip.VerticalOffset = y;
        OverviewTip.IsOpen = true;
    }
    private void Dashboard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ClosePeriodPicker();
        _hoveredRow = null;
        _model?.ClearDistributionSelection();
        CloseTip();
    }
    private void Dashboard_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ClosePeriodPicker();
        _hoveredRow = null;
        _model?.ClearDistributionSelection();
        CloseTip();
        e.Handled = true;
    }
    private void OverviewScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.HorizontalChange != 0) { ClosePeriodPicker(); CloseTip(); }
    }
    private void CloseTip()
    {
        _tipRefreshOperation?.Abort();
        _tipRefreshOperation = null;
        OverviewTip.IsOpen = false;
        RemoveTipHook();
    }
    private void OverviewTip_Opened(object? sender, EventArgs e)
    {
        RemoveTipHook();
        _tipSource = PresentationSource.FromVisual(OverviewTipSurface) as HwndSource;
        _tipSource?.AddHook(TipWindowMessages);
    }
    private void OverviewTip_Closed(object? sender, EventArgs e) => RemoveTipHook();
    private void RemoveTipHook() { _tipSource?.RemoveHook(TipWindowMessages); _tipSource = null; }
    private static IntPtr TipWindowMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0084) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1); // Transparent native popup cannot steal the chart's mouse hover.
    }
}
