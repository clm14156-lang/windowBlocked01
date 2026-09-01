using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class SettingsPage : UserControl
{
    private readonly DispatcherTimer _forcedModeVipGuideOpenTimer;
    private readonly DispatcherTimer _forcedModeVipGuideCloseTimer;
    private bool _isForcedModeVipHoverTargetHovered;
    private bool _isForcedModeVipGuideHovered;

    public SettingsPage()
    {
        _forcedModeVipGuideOpenTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _forcedModeVipGuideOpenTimer.Tick += ForcedModeVipGuideOpenTimer_Tick;
        _forcedModeVipGuideCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _forcedModeVipGuideCloseTimer.Tick += ForcedModeVipGuideCloseTimer_Tick;
        InitializeComponent();
        ForcedModeVipGuidePopup.CustomPopupPlacementCallback = PlaceForcedModeVipGuidePopup;
        DataContextChanged += SettingsPage_DataContextChanged;
        Unloaded += SettingsPage_Unloaded;
    }

    private void ForcedModeVipHoverTarget_MouseEnter(object sender, MouseEventArgs e)
    {
        _isForcedModeVipHoverTargetHovered = true;
        _forcedModeVipGuideCloseTimer.Stop();
        if (DataContext is SettingsPageViewModel { CanUseForcedMode: false })
        {
            _forcedModeVipGuideOpenTimer.Stop();
            _forcedModeVipGuideOpenTimer.Start();
        }
    }

    private void ForcedModeVipHoverTarget_MouseLeave(object sender, MouseEventArgs e)
    {
        _isForcedModeVipHoverTargetHovered = false;
        _forcedModeVipGuideOpenTimer.Stop();
        ScheduleForcedModeVipGuideClose();
    }

    private void ForcedModeVipGuide_MouseEnter(object sender, MouseEventArgs e)
    {
        _isForcedModeVipGuideHovered = true;
        _forcedModeVipGuideCloseTimer.Stop();
    }

    private void ForcedModeVipGuide_MouseLeave(object sender, MouseEventArgs e)
    {
        _isForcedModeVipGuideHovered = false;
        ScheduleForcedModeVipGuideClose();
    }

    private void ForcedModeVipGuideOpenTimer_Tick(object? sender, EventArgs e)
    {
        _forcedModeVipGuideOpenTimer.Stop();
        if (_isForcedModeVipHoverTargetHovered &&
            DataContext is SettingsPageViewModel { CanUseForcedMode: false })
        {
            ForcedModeVipGuidePopup.IsOpen = true;
        }
    }

    private void ForcedModeVipGuideCloseTimer_Tick(object? sender, EventArgs e)
    {
        _forcedModeVipGuideCloseTimer.Stop();
        if (!_isForcedModeVipHoverTargetHovered && !_isForcedModeVipGuideHovered)
        {
            ForcedModeVipGuidePopup.IsOpen = false;
        }
    }

    private void ScheduleForcedModeVipGuideClose()
    {
        _forcedModeVipGuideCloseTimer.Stop();
        _forcedModeVipGuideCloseTimer.Start();
    }

    private CustomPopupPlacement[] PlaceForcedModeVipGuidePopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double gap = 4;
        const double boundaryPadding = 8;
        var x = (targetSize.Width - popupSize.Width) / 2;
        var y = targetSize.Height + gap;
        var placeAbove = false;
        var window = Window.GetWindow(this);
        if (window is not null && window.ActualWidth > 0 && window.ActualHeight > 0)
        {
            var targetOrigin = ForcedModeVipHoverTarget.TranslatePoint(new Point(0, 0), window);
            var clampedLeft = Math.Clamp(
                targetOrigin.X + x,
                boundaryPadding,
                Math.Max(boundaryPadding, window.ActualWidth - popupSize.Width - boundaryPadding));
            x = clampedLeft - targetOrigin.X;
            if (targetOrigin.Y + y + popupSize.Height > window.ActualHeight - boundaryPadding)
            {
                y = -popupSize.Height - gap;
                placeAbove = true;
            }
        }

        ForcedModeVipGuideCard.VerticalAlignment = placeAbove
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        ForcedModeVipGuideTopArrow.Visibility = placeAbove ? Visibility.Collapsed : Visibility.Visible;
        ForcedModeVipGuideBottomArrow.Visibility = placeAbove ? Visibility.Visible : Visibility.Collapsed;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
    }

    private void SettingsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsPageViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= SettingsViewModel_PropertyChanged;
            oldViewModel.AutomaticRuleFocusRequested -= SettingsViewModel_AutomaticRuleFocusRequested;
        }

        if (e.NewValue is SettingsPageViewModel newViewModel)
        {
            newViewModel.PropertyChanged += SettingsViewModel_PropertyChanged;
            newViewModel.AutomaticRuleFocusRequested += SettingsViewModel_AutomaticRuleFocusRequested;
            if (newViewModel.CanUseForcedMode)
            {
                CloseForcedModeVipGuide();
            }
        }
    }

    private void SettingsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.CanUseForcedMode) &&
            sender is SettingsPageViewModel { CanUseForcedMode: true })
        {
            CloseForcedModeVipGuide();
        }
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e) => CloseForcedModeVipGuide();

    private void CloseForcedModeVipGuide()
    {
        _forcedModeVipGuideOpenTimer.Stop();
        _forcedModeVipGuideCloseTimer.Stop();
        _isForcedModeVipHoverTargetHovered = false;
        _isForcedModeVipGuideHovered = false;
        ForcedModeVipGuidePopup.IsOpen = false;
    }

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
