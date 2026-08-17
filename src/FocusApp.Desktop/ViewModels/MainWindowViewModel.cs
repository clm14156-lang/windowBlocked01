using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private NavigationItemViewModel _currentNavigationItem;

    public MainWindowViewModel(
        IEnumerable<NavigationItemViewModel> primaryNavigationItems,
        NavigationItemViewModel accountNavigationItem,
        HomePageViewModel homePage,
        SettingsPageViewModel? settingsPage = null,
        BlockingPageViewModel? blockingPage = null)
    {
        PrimaryNavigationItems = new ReadOnlyCollection<NavigationItemViewModel>(
            primaryNavigationItems.ToList());

        if (PrimaryNavigationItems.Count == 0)
        {
            throw new ArgumentException("At least one primary navigation item is required.", nameof(primaryNavigationItems));
        }

        AccountNavigationItem = accountNavigationItem;
        HomePage = homePage;
        SettingsPage = settingsPage ?? new SettingsPageViewModel([], []);
        BlockingPage = blockingPage ?? new BlockingPageViewModel([], [], "Added websites: {0}", "Added applications: {0}");
        _currentNavigationItem = PrimaryNavigationItems[0];
        _currentNavigationItem.IsSelected = true;
        NavigateCommand = new RelayCommand<NavigationItemViewModel>(Navigate);
        OpenAuthCommand = new RelayCommand<object>(_ => AuthModal.OpenLogin());
        ToggleThemePanelCommand = new RelayCommand<object>(_ => ThemePanel.Toggle());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyCollection<NavigationItemViewModel> PrimaryNavigationItems { get; }

    public NavigationItemViewModel AccountNavigationItem { get; }

    public ICommand NavigateCommand { get; }

    public ICommand OpenAuthCommand { get; }

    public ICommand ToggleThemePanelCommand { get; }

    public AuthModalViewModel AuthModal { get; } = new();

    public ThemePanelViewModel ThemePanel { get; } = new();

    public HomePageViewModel HomePage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public BlockingPageViewModel BlockingPage { get; }

    public string CurrentPageTitle => _currentNavigationItem.Title;

    public NavigationPage CurrentPage => _currentNavigationItem.Page;

    private void Navigate(NavigationItemViewModel? destination)
    {
        if (destination is null || ReferenceEquals(destination, _currentNavigationItem))
        {
            return;
        }

        _currentNavigationItem.IsSelected = false;
        _currentNavigationItem = destination;
        _currentNavigationItem.IsSelected = true;
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
