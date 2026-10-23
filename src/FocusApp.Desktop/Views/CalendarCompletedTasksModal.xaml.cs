using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CalendarCompletedTasksModal : UserControl
{
    private IInputElement? _previousFocus;

    public CalendarCompletedTasksModal()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _previousFocus = Keyboard.FocusedElement;
                CompletedTasksScrollViewer.ScrollToTop();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) CloseModalButton.Focus(); }));
            }
            else
            {
                if (_previousFocus is UIElement { IsVisible: true } element) element.Focus();
                _previousFocus = null;
            }
        };
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Overlay)) Close();
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private void Close()
    {
        if (DataContext is StatisticsOverviewViewModel model) model.CloseCalendarCompletedTasksCommand.Execute(null);
    }
}
