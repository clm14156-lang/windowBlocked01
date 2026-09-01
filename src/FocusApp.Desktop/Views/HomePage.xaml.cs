using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class HomePage : UserControl
{
    private readonly DispatcherTimer _automaticBlockingPopupOpenTimer;
    private readonly DispatcherTimer _automaticBlockingPopupCloseTimer;

    public HomePage()
    {
        _automaticBlockingPopupOpenTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _automaticBlockingPopupOpenTimer.Tick += AutomaticBlockingPopupOpenTimer_Tick;
        _automaticBlockingPopupCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _automaticBlockingPopupCloseTimer.Tick += AutomaticBlockingPopupCloseTimer_Tick;
        InitializeComponent();
        AutomaticBlockingPopup.CustomPopupPlacementCallback = PlaceAutomaticBlockingTooltip;
        Unloaded += HomePage_Unloaded;
    }

    private void AutomaticBlockingTimeText_MouseEnter(object sender, MouseEventArgs e)
    {
        _automaticBlockingPopupCloseTimer.Stop();
        _automaticBlockingPopupOpenTimer.Stop();
        _automaticBlockingPopupOpenTimer.Start();
    }

    private void AutomaticBlockingTimeText_MouseLeave(object sender, MouseEventArgs e)
    {
        _automaticBlockingPopupOpenTimer.Stop();
        ScheduleAutomaticBlockingPopupClose();
    }

    private void AutomaticBlockingPopup_MouseEnter(object sender, MouseEventArgs e)
        => _automaticBlockingPopupCloseTimer.Stop();

    private void AutomaticBlockingPopup_MouseLeave(object sender, MouseEventArgs e)
        => ScheduleAutomaticBlockingPopupClose();

    private void AutomaticBlockingPopupOpenTimer_Tick(object? sender, EventArgs e)
    {
        _automaticBlockingPopupOpenTimer.Stop();
        if (AutomaticBlockingTimeText.IsMouseOver)
        {
            AutomaticBlockingPopup.IsOpen = true;
        }
    }

    private void AutomaticBlockingPopupCloseTimer_Tick(object? sender, EventArgs e)
    {
        _automaticBlockingPopupCloseTimer.Stop();
        if (!AutomaticBlockingTimeText.IsMouseOver && !AutomaticBlockingPopupRoot.IsMouseOver)
        {
            AutomaticBlockingPopup.IsOpen = false;
        }
    }

    private void ScheduleAutomaticBlockingPopupClose()
    {
        _automaticBlockingPopupCloseTimer.Stop();
        _automaticBlockingPopupCloseTimer.Start();
    }

    private void ManageAutomaticRule_Click(object sender, RoutedEventArgs e)
    {
        AutomaticBlockingPopup.IsOpen = false;
        if (DataContext is HomePageViewModel viewModel &&
            viewModel.ManageNextAutomaticRuleCommand.CanExecute(null))
        {
            viewModel.ManageNextAutomaticRuleCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _automaticBlockingPopupOpenTimer.Stop();
        _automaticBlockingPopupCloseTimer.Stop();
        AutomaticBlockingPopup.IsOpen = false;
    }

    private static CustomPopupPlacement[] PlaceAutomaticBlockingTooltip(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double targetGap = 8;
        return
        [
            new CustomPopupPlacement(
                new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - targetGap),
                PopupPrimaryAxis.Horizontal)
        ];
    }
}
