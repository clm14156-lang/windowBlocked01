using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class HomePage : UserControl
{
    private readonly DispatcherTimer _automaticRulePopupCloseTimer;

    public HomePage()
    {
        InitializeComponent();
        _automaticRulePopupCloseTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _automaticRulePopupCloseTimer.Tick += (_, _) =>
        {
            _automaticRulePopupCloseTimer.Stop();
            if (!NextAutomaticBlockingHelpButton.IsMouseOver && !NextAutomaticBlockingPopup.IsMouseOver)
            {
                NextAutomaticBlockingPopup.IsOpen = false;
            }
        };
        Unloaded += (_, _) =>
        {
            (DataContext as HomePageViewModel)?.CancelPendingFocusStart();
            _automaticRulePopupCloseTimer.Stop();
            NextAutomaticBlockingPopup.IsOpen = false;
        };
        DataContextChanged += (_, args) => (args.OldValue as HomePageViewModel)?.CancelPendingFocusStart();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is false) (DataContext as HomePageViewModel)?.CancelPendingFocusStart();
        };
    }

    private void NextAutomaticBlockingHelp_MouseEnter(object sender, MouseEventArgs e)
    {
        _automaticRulePopupCloseTimer.Stop();
        if (DataContext is HomePageViewModel { HasNextAutomaticRule: true } viewModel)
        {
            viewModel.RefreshNextAutomaticRuleCountdown();
            NextAutomaticBlockingPopup.IsOpen = true;
        }
    }

    private void NextAutomaticBlockingHelp_MouseLeave(object sender, MouseEventArgs e)
        => _automaticRulePopupCloseTimer.Start();

    private void NextAutomaticBlockingPopup_MouseEnter(object sender, MouseEventArgs e)
        => _automaticRulePopupCloseTimer.Stop();

    private void NextAutomaticBlockingPopup_MouseLeave(object sender, MouseEventArgs e)
        => _automaticRulePopupCloseTimer.Start();

    private void NextAutomaticBlockingCard_Click(object sender, RoutedEventArgs e)
    {
        _automaticRulePopupCloseTimer.Stop();
        NextAutomaticBlockingPopup.IsOpen = false;
    }

}
