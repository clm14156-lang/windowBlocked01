using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class ThemePanel : UserControl
{
    public ThemePanel()
    {
        InitializeComponent();
    }

    private void PremiumThemeOption_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ThemePanelViewModel { CanUsePremiumThemes: false } &&
            !IsWithinPreviewButton(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
        }
    }

    private static bool IsWithinPreviewButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button { Name: "PreviewThemeButton" })
            {
                return true;
            }

            if (source is RadioButton)
            {
                return false;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
