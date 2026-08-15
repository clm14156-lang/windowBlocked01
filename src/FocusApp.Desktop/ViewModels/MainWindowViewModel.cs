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
        HomePageViewModel homePage)
    {
        PrimaryNavigationItems = new ReadOnlyCollection<NavigationItemViewModel>(
            primaryNavigationItems.ToList());

        if (PrimaryNavigationItems.Count == 0)
        {
            throw new ArgumentException("At least one primary navigation item is required.", nameof(primaryNavigationItems));
        }

        AccountNavigationItem = accountNavigationItem;
        HomePage = homePage;
        _currentNavigationItem = PrimaryNavigationItems[0];
        _currentNavigationItem.IsSelected = true;
        NavigateCommand = new RelayCommand<NavigationItemViewModel>(Navigate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyCollection<NavigationItemViewModel> PrimaryNavigationItems { get; }

    public NavigationItemViewModel AccountNavigationItem { get; }

    public ICommand NavigateCommand { get; }

    public HomePageViewModel HomePage { get; }

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
