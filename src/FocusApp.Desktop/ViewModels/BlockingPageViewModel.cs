using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public enum BlockingTab
{
    Websites,
    Applications
}

public sealed class BlockingPageViewModel : INotifyPropertyChanged
{
    private readonly string _websiteCountTemplate;
    private readonly string _applicationCountTemplate;
    private BlockingTab _selectedTab = BlockingTab.Websites;

    public BlockingPageViewModel(
        IEnumerable<BlockingWebsiteItemViewModel> websites,
        IEnumerable<BlockingApplicationItemViewModel> applications,
        string websiteCountTemplate,
        string applicationCountTemplate)
    {
        Websites = new ObservableCollection<BlockingWebsiteItemViewModel>(websites);
        Applications = new ObservableCollection<BlockingApplicationItemViewModel>(applications);
        _websiteCountTemplate = websiteCountTemplate;
        _applicationCountTemplate = applicationCountTemplate;

        SelectWebsitesCommand = new RelayCommand<object>(_ => SelectedTab = BlockingTab.Websites);
        SelectApplicationsCommand = new RelayCommand<object>(_ => SelectedTab = BlockingTab.Applications);
        OpenWebsiteModalCommand = new RelayCommand<object>(_ => WebsiteModal.Open());
        OpenProgramModalCommand = new RelayCommand<object>(_ => ProgramModal.Open());
        DeleteWebsiteCommand = new RelayCommand<BlockingWebsiteItemViewModel>(DeleteWebsite);
        DeleteApplicationCommand = new RelayCommand<BlockingApplicationItemViewModel>(DeleteApplication);
        EditWebsiteCommand = new RelayCommand<BlockingWebsiteItemViewModel>(_ => { });

        WebsiteModal.WebsiteCreated += WebsiteModal_WebsiteCreated;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<BlockingWebsiteItemViewModel> Websites { get; }

    public ObservableCollection<BlockingApplicationItemViewModel> Applications { get; }

    public AddWebsiteModalViewModel WebsiteModal { get; } = new();

    public AddProgramModalViewModel ProgramModal { get; } = new();

    public ICommand SelectWebsitesCommand { get; }

    public ICommand SelectApplicationsCommand { get; }

    public ICommand OpenWebsiteModalCommand { get; }

    public ICommand OpenProgramModalCommand { get; }

    public ICommand DeleteWebsiteCommand { get; }

    public ICommand DeleteApplicationCommand { get; }

    public ICommand EditWebsiteCommand { get; }

    public BlockingTab SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (_selectedTab == value)
            {
                return;
            }

            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWebsitesSelected));
            OnPropertyChanged(nameof(IsApplicationsSelected));
        }
    }

    public bool IsWebsitesSelected => SelectedTab == BlockingTab.Websites;

    public bool IsApplicationsSelected => SelectedTab == BlockingTab.Applications;

    public string WebsiteCountText => string.Format(_websiteCountTemplate, Websites.Count);

    public string ApplicationCountText => string.Format(_applicationCountTemplate, Applications.Count);

    private void WebsiteModal_WebsiteCreated(object? sender, WebsiteDraft draft)
    {
        Websites.Add(new BlockingWebsiteItemViewModel(Guid.NewGuid(), draft.Name, draft.Address, true));
        OnPropertyChanged(nameof(WebsiteCountText));
    }

    private void DeleteWebsite(BlockingWebsiteItemViewModel? website)
    {
        if (website is not null && Websites.Remove(website))
        {
            OnPropertyChanged(nameof(WebsiteCountText));
        }
    }

    private void DeleteApplication(BlockingApplicationItemViewModel? application)
    {
        if (application is not null && Applications.Remove(application))
        {
            OnPropertyChanged(nameof(ApplicationCountText));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BlockingWebsiteItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;

    public BlockingWebsiteItemViewModel(Guid id, string name, string address, bool isEnabled)
    {
        Id = id;
        Name = name;
        Address = address;
        _isEnabled = isEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Name { get; }

    public string Address { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
}

public sealed class BlockingApplicationItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;

    public BlockingApplicationItemViewModel(Guid id, string name, string path, bool isEnabled)
    {
        Id = id;
        Name = name;
        Path = path;
        _isEnabled = isEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Name { get; }

    public string Path { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
}
