using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusGoalSettingsModalInteractionTests
{
    [Fact]
    public void ClickingMoreButtonOpensDeletePopupForSavedTarget()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new FocusGoalSettingsModalViewModel();
                viewModel.SaveCommand.Execute(null);
                viewModel.OpenCommand.Execute(null);
                var modal = new FocusGoalSettingsModal { DataContext = viewModel };
                var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, Content = modal, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();

                var button = Assert.IsType<Button>(modal.FindName("FocusGoalMoreButton"));
                var popup = Assert.IsType<Popup>(modal.FindName("FocusGoalMoreMenu"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                Assert.True(viewModel.IsMoreMenuOpen);
                Assert.True(popup.IsOpen);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(viewModel.IsMoreMenuOpen);
                Assert.False(popup.IsOpen);
                popup.IsOpen = false;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
