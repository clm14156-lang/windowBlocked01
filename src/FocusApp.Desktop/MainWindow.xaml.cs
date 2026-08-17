using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_DataContextChanged;
    }

    private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel oldViewModel)
        {
            oldViewModel.ThemePanel.ThemeSelected -= ThemePanel_ThemeSelected;
        }

        if (e.NewValue is MainWindowViewModel newViewModel)
        {
            newViewModel.ThemePanel.ThemeSelected += ThemePanel_ThemeSelected;
        }
    }

    private static void ThemePanel_ThemeSelected(object? sender, string themeKey)
    {
        var (accent, start, end) = themeKey switch
        {
            "Blue" => ("#3989EF", "#64ACFF", "#2875DF"),
            "Cyan" => ("#39C4CC", "#65DBDF", "#20AAB7"),
            "Dark" => ("#303030", "#4A4A4A", "#161616"),
            "Warm" => ("#FF7A68", "#FF6678", "#FFB15E"),
            "Sky" => ("#438BF1", "#3983F4", "#8FD5FF"),
            "Dream" => ("#8A60EE", "#7352EE", "#D99DEA"),
            "Fresh" => ("#31BFB7", "#2AC3C9", "#91E7B0"),
            "Starry" => ("#5F48B8", "#6F55C8", "#14255C"),
            "Mountain" => ("#C98269", "#D49AAF", "#8D513E"),
            "Forest" => ("#32745A", "#79A28E", "#174D33"),
            "Snow" => ("#5B9DCF", "#4FADE6", "#AACFE8"),
            "Moon" => ("#286497", "#4E8DC0", "#072D58"),
            _ => ("#FF7A00", "#FFB43A", "#FF6900")
        };

        var accentColor = ParseColor(accent);
        var startColor = ParseColor(start);
        var endColor = ParseColor(end);
        var resources = Application.Current.Resources;

        resources["AccentPrimary"] = new SolidColorBrush(accentColor);
        resources["AccentHover"] = new SolidColorBrush(startColor);
        resources["AccentPressed"] = new SolidColorBrush(endColor);
        resources["AccentTint"] = new SolidColorBrush(Color.FromArgb(28, accentColor.R, accentColor.G, accentColor.B));
        resources["AccentShadowColor"] = accentColor;
        resources["FocusButtonBackground"] = CreateGradient(startColor, endColor);
        resources["FocusButtonHoverBackground"] = CreateGradient(startColor, accentColor);
        resources["FocusButtonPressedBackground"] = new SolidColorBrush(endColor);
    }

    private static Color ParseColor(string value)
    {
        return (Color)ColorConverter.ConvertFromString(value);
    }

    private static LinearGradientBrush CreateGradient(Color start, Color end)
    {
        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new(start, 0),
                new(end, 1)
            },
            new Point(0, 0),
            new Point(1, 1));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsWithinButton(e.OriginalSource as DependencyObject) &&
            e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.ToggleThemePanelCommand.Execute(null);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AuthScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
        {
            if (DataContext is ViewModels.MainWindowViewModel viewModel)
            {
                viewModel.AuthModal.CloseCommand.Execute(null);
            }
        }
    }

    private void RuleScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.SettingsPage.RuleModal.CloseCommand.Execute(null);
        }
    }

    private void WebsiteScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.BlockingPage.WebsiteModal.CloseCommand.Execute(null);
        }
    }

    private void ProgramScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.BlockingPage.ProgramModal.CloseCommand.Execute(null);
        }
    }

    private void FocusTargetScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.HomePage.FocusTargetModal.CloseCommand.Execute(null);
        }
    }

    private void VipScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.VipModal.CloseCommand.Execute(null);
        }
    }

    private void MembershipCenterScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender) &&
            DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.MembershipCenter.CloseCommand.Execute(null);
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.HomePage.CustomTimeModal.IsOpen && !CustomTimeModalControl.IsMouseOver)
        {
            viewModel.HomePage.CustomTimeModal.CancelCommand.Execute(null);
        }

        if (viewModel.ThemePanel.IsOpen &&
            !ThemePanelControl.IsMouseOver &&
            !ThemeButton.IsMouseOver)
        {
            viewModel.ThemePanel.CloseCommand.Execute(null);
        }

        if (viewModel.IsAccountPanelOpen &&
            !AccountPanelControl.IsMouseOver &&
            !AccountButton.IsMouseOver)
        {
            viewModel.CloseAccountPanel();
        }
    }
}
