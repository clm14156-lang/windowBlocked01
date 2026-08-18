using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public enum BlockedContentTab
{
    All,
    Websites,
    Applications
}

public sealed class BlockedContentModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private BlockedContentTab _selectedTab;

    public BlockedContentModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SelectAllCommand = new RelayCommand<object>(_ => SelectedTab = BlockedContentTab.All);
        SelectWebsitesCommand = new RelayCommand<object>(_ => SelectedTab = BlockedContentTab.Websites);
        SelectApplicationsCommand = new RelayCommand<object>(_ => SelectedTab = BlockedContentTab.Applications);
        DisableCommand = new RelayCommand<BlockingContentItemViewModel>(Disable);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BlockingContentItemViewModel> AllItems { get; } = [];
    public ObservableCollection<BlockingContentItemViewModel> WebsiteItems { get; } = [];
    public ObservableCollection<BlockingContentItemViewModel> ApplicationItems { get; } = [];
    public ObservableCollection<BlockingContentItemViewModel> VisibleItems { get; } = [];

    public ICommand CloseCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectWebsitesCommand { get; }
    public ICommand SelectApplicationsCommand { get; }
    public ICommand DisableCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(); } }
    }

    public BlockedContentTab SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (_selectedTab == value) return;
            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(IsWebsitesSelected));
            OnPropertyChanged(nameof(IsApplicationsSelected));
            RefreshVisibleItems();
        }
    }

    public bool IsAllSelected => SelectedTab == BlockedContentTab.All;
    public bool IsWebsitesSelected => SelectedTab == BlockedContentTab.Websites;
    public bool IsApplicationsSelected => SelectedTab == BlockedContentTab.Applications;

    public int AllCount => AllItems.Count;
    public int WebsiteCount => WebsiteItems.Count;
    public int ApplicationCount => ApplicationItems.Count;
    public string AllTabText => $"全部 ({AllCount})";
    public string WebsiteTabText => $"网站 ({WebsiteCount})";
    public string ApplicationTabText => $"软件 ({ApplicationCount})";

    public void Open() { SelectedTab = BlockedContentTab.All; IsOpen = true; }

    public void Update(IEnumerable<BlockingWebsiteItemViewModel> websites, IEnumerable<BlockingApplicationItemViewModel> applications)
    {
        foreach (var item in AllItems) item.Dispose();
        AllItems.Clear(); WebsiteItems.Clear(); ApplicationItems.Clear();
        foreach (var website in websites.Where(item => item.IsEnabled))
        {
            var item = new BlockingContentItemViewModel(website);
            WebsiteItems.Add(item); AllItems.Add(item);
        }
        foreach (var application in applications.Where(item => item.IsEnabled))
        {
            var item = new BlockingContentItemViewModel(application);
            ApplicationItems.Add(item); AllItems.Add(item);
        }
        RefreshVisibleItems();
        OnPropertyChanged(nameof(AllCount)); OnPropertyChanged(nameof(WebsiteCount)); OnPropertyChanged(nameof(ApplicationCount));
        OnPropertyChanged(nameof(AllTabText)); OnPropertyChanged(nameof(WebsiteTabText)); OnPropertyChanged(nameof(ApplicationTabText));
    }

    private void Disable(BlockingContentItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        // Remove the projection first so the click has an immediate visual result.
        AllItems.Remove(item);
        WebsiteItems.Remove(item);
        ApplicationItems.Remove(item);
        VisibleItems.Remove(item);
        item.Dispose();
        OnPropertyChanged(nameof(AllCount)); OnPropertyChanged(nameof(WebsiteCount)); OnPropertyChanged(nameof(ApplicationCount));
        OnPropertyChanged(nameof(AllTabText)); OnPropertyChanged(nameof(WebsiteTabText)); OnPropertyChanged(nameof(ApplicationTabText));
        item.Disable();
    }

    private void RefreshVisibleItems()
    {
        VisibleItems.Clear();
        var source = SelectedTab switch
        {
            BlockedContentTab.Websites => WebsiteItems,
            BlockedContentTab.Applications => ApplicationItems,
            _ => AllItems
        };
        foreach (var item in source) VisibleItems.Add(item);
    }
    private void Close() => IsOpen = false;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
