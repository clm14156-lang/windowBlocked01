using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class SettingsPage : UserControl
{
    private readonly DispatcherTimer _extensionRefresh = new() { Interval = TimeSpan.FromSeconds(3) };
    public SettingsPage()
    {
        InitializeComponent();
        DataContextChanged += SettingsPage_DataContextChanged;
        _extensionRefresh.Tick += async (_, _) => await RefreshExtensionAsync();
        Loaded += (_, _) => UpdateExtensionMonitoring();
        IsVisibleChanged += (_, _) => UpdateExtensionMonitoring();
        Unloaded += (_, _) => { _extensionRefresh.Stop(); CloseVipFeatureHint(); };
    }

    private void UpdateExtensionMonitoring()
    {
        if (!IsLoaded || !IsVisible) { _extensionRefresh.Stop(); return; }
        _extensionRefresh.Start();
        _ = RefreshExtensionAsync();
    }

    private Task RefreshExtensionAsync()
        => IsVisible && DataContext is SettingsPageViewModel { BrowserExtension: { } extension }
            ? extension.RefreshInstallationAsync() : Task.CompletedTask;

    private void SettingsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        CloseVipFeatureHint();
        if (e.OldValue is SettingsPageViewModel oldViewModel)
        {
            oldViewModel.AutomaticRuleFocusRequested -= SettingsViewModel_AutomaticRuleFocusRequested;
        }

        if (e.NewValue is SettingsPageViewModel newViewModel)
        {
            newViewModel.AutomaticRuleFocusRequested += SettingsViewModel_AutomaticRuleFocusRequested;
        }
        UpdateExtensionMonitoring();
    }

    private void FocusThemeOption_Click(object sender, RoutedEventArgs e) =>
        FocusThemePreviewSelector.IsChecked = false;

    private void RuleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToggleButton moreButton })
        {
            moreButton.IsChecked = false;
        }
    }

    private void RuleSwitch_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ToggleButton { DataContext: AutomaticRuleItemViewModel rule } &&
            DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.ToggleRuleCommand.Execute(rule);
            e.Handled = true;
        }
    }

    private void SettingsViewModel_AutomaticRuleFocusRequested(AutomaticRuleItemViewModel rule)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                AutomaticRulesList.UpdateLayout();
                if (AutomaticRulesList.ItemContainerGenerator.ContainerFromItem(rule) is FrameworkElement container)
                {
                    container.BringIntoView();
                }
            }));
    }

    private void AutomaticRuleRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border { DataContext: AutomaticRuleItemViewModel rule })
        {
            rule.ConsumeNavigationHighlight();
        }
    }
}
