using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AccountSyncModal : UserControl
{
    public AccountSyncModal()
    {
        InitializeComponent();
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sectionKey })
        {
            return;
        }

        FrameworkElement target = sectionKey switch
        {
            AccountSyncModalViewModel.DevicesSection => DevicesSectionAnchor,
            AccountSyncModalViewModel.SecuritySection => SecuritySectionAnchor,
            _ => CloudSectionAnchor
        };

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => target.BringIntoView());
    }

    private void AccountSyncModal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is AccountSyncModalViewModel viewModel)
        {
            viewModel.SelectSection(AccountSyncModalViewModel.CloudSection);
            ContentScrollViewer.ScrollToTop();
        }
    }
}
