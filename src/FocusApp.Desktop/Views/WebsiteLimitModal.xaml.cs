using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class WebsiteLimitModal : UserControl
{
    public WebsiteLimitModal() => InitializeComponent();

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) CloseButton.Focus();
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not BlockingPageViewModel viewModel) return;
        viewModel.CloseWebsiteLimitCommand.Execute(null);
        e.Handled = true;
    }
}
