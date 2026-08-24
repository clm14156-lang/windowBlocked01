using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.Services;

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
    private readonly IFaviconService _faviconService;
    private BlockingTab _selectedTab = BlockingTab.Websites;

    public BlockingPageViewModel(
        IEnumerable<BlockingWebsiteItemViewModel> websites,
        IEnumerable<BlockingApplicationItemViewModel> applications,
        string websiteCountTemplate,
        string applicationCountTemplate,
        IFaviconService? faviconService = null,
        IEnumerable<RecentProgramRecord>? recentPrograms = null)
    {
        Websites = new ObservableCollection<BlockingWebsiteItemViewModel>(websites);
        Applications = new ObservableCollection<BlockingApplicationItemViewModel>(applications);
        _websiteCountTemplate = websiteCountTemplate;
        _applicationCountTemplate = applicationCountTemplate;
        _faviconService = faviconService ?? new FaviconService();
        ProgramModal = new AddProgramModalViewModel(recentPrograms);

        SelectWebsitesCommand = new RelayCommand<object>(_ => SelectedTab = BlockingTab.Websites);
        SelectApplicationsCommand = new RelayCommand<object>(_ => SelectedTab = BlockingTab.Applications);
        OpenWebsiteModalCommand = new RelayCommand<object>(_ => WebsiteModal.Open());
        OpenProgramModalCommand = new RelayCommand<object>(_ => ProgramModal.Open());
        DeleteWebsiteCommand = new RelayCommand<BlockingWebsiteItemViewModel>(DeleteWebsite);
        DeleteApplicationCommand = new RelayCommand<BlockingApplicationItemViewModel>(DeleteApplication);
        EditWebsiteCommand = new RelayCommand<BlockingWebsiteItemViewModel>(EditWebsite);

        WebsiteModal.WebsiteCreated += WebsiteModal_WebsiteCreated;
        WebsiteModal.WebsiteUpdated += WebsiteModal_WebsiteUpdated;
        ProgramModal.ProgramSelected += ProgramModal_ProgramSelected;
        foreach (var website in Websites) website.PropertyChanged += Website_PropertyChanged;
        foreach (var application in Applications) application.PropertyChanged += Application_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? BlockingChanged;

    public ObservableCollection<BlockingWebsiteItemViewModel> Websites { get; }

    public ObservableCollection<BlockingApplicationItemViewModel> Applications { get; }

    public AddWebsiteModalViewModel WebsiteModal { get; } = new();

    public AddProgramModalViewModel ProgramModal { get; }

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
        var item = new BlockingWebsiteItemViewModel(Guid.NewGuid(), draft.Name, draft.Address, true);
        item.PropertyChanged += Website_PropertyChanged;
        Websites.Add(item);
        OnPropertyChanged(nameof(WebsiteCountText));
        BlockingChanged?.Invoke(this, EventArgs.Empty);
        _ = LoadFaviconAsync(item);
    }

    private void WebsiteModal_WebsiteUpdated(object? sender, WebsiteEdit edit)
    {
        var item = Websites.FirstOrDefault(website => website.Id == edit.WebsiteId);
        if (item is null)
        {
            return;
        }

        var addressChanged = !string.Equals(item.Address, edit.Draft.Address, StringComparison.Ordinal);
        item.UpdateDetails(edit.Draft.Name, edit.Draft.Address);
        if (addressChanged)
        {
            item.Favicon = null;
            _ = LoadFaviconAsync(item);
        }

        BlockingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ProgramModal_ProgramSelected(object? sender, RecentProgramRecordViewModel program)
    {
        if (Applications.Any(application => string.Equals(application.Path, program.ExePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var item = new BlockingApplicationItemViewModel(Guid.NewGuid(), program.DisplayName, program.ExePath, true);
        item.PropertyChanged += Application_PropertyChanged;
        Applications.Add(item);
        OnPropertyChanged(nameof(ApplicationCountText));
        BlockingChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadFaviconAsync(BlockingWebsiteItemViewModel item)
    {
        try
        {
            var favicon = await _faviconService.GetFaviconAsync(item.Address);
            if (favicon is not null && Websites.Contains(item))
            {
                item.Favicon = favicon;
            }
        }
        catch
        {
            // A missing favicon must not affect creation of the website rule.
        }
    }

    private void DeleteWebsite(BlockingWebsiteItemViewModel? website)
    {
        if (website is not null && Websites.Remove(website))
        {
            website.PropertyChanged -= Website_PropertyChanged;
            OnPropertyChanged(nameof(WebsiteCountText));
            BlockingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void EditWebsite(BlockingWebsiteItemViewModel? website)
    {
        if (website is not null && Websites.Contains(website))
        {
            website.IsActionMenuOpen = false;
            WebsiteModal.OpenForEdit(website.Id, website.Name, website.Address);
        }
    }

    private void DeleteApplication(BlockingApplicationItemViewModel? application)
    {
        if (application is not null && Applications.Remove(application))
        {
            application.PropertyChanged -= Application_PropertyChanged;
            OnPropertyChanged(nameof(ApplicationCountText));
            BlockingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Website_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BlockingWebsiteItemViewModel.IsEnabled) or nameof(BlockingWebsiteItemViewModel.Favicon))
        {
            BlockingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Application_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlockingApplicationItemViewModel.IsEnabled))
        {
            BlockingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class BlockingWebsiteItemViewModel : INotifyPropertyChanged
{
    private string _name;
    private string _address;
    private bool _isActionMenuOpen;
    private bool _isEnabled;
    private ImageSource? _favicon;

    public BlockingWebsiteItemViewModel(Guid id, string name, string address, bool isEnabled)
    {
        Id = id;
        _name = name;
        _address = address;
        _isEnabled = isEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Name => _name;

    public string Address => _address;

    public bool IsActionMenuOpen
    {
        get => _isActionMenuOpen;
        set
        {
            if (_isActionMenuOpen == value)
            {
                return;
            }

            _isActionMenuOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActionMenuOpen)));
        }
    }

    public void UpdateDetails(string name, string address)
    {
        if (_name != name)
        {
            _name = name;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }

        if (_address != address)
        {
            _address = address;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Address)));
        }
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        set
        {
            if (ReferenceEquals(_favicon, value))
            {
                return;
            }

            _favicon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Favicon)));
        }
    }

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
