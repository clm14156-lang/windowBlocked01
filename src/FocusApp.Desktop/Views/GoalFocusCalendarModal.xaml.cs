using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class GoalFocusCalendarModal : UserControl
{
    public GoalFocusCalendarModal() => InitializeComponent();

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) && DataContext is GoalFocusCalendarViewModel model)
            model.Close();
    }

    private void Modal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not GoalFocusCalendarViewModel model) return;
        model.Close();
        e.Handled = true;
    }

    private void Modal_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) CloseCalendarButton.Focus(); }));
    }
}
