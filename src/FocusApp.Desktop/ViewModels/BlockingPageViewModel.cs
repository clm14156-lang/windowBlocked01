using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Core;
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
    private readonly IWebsiteMetadataService _websiteMetadataService;
    private readonly IProgramIconService _programIconService;
    private readonly AccessControlService _accessControlService;
    private BlockingTab _selectedTab = BlockingTab.Websites;
    private bool _isUpdatingWebsiteDetails;

    public BlockingPageViewModel(
        IEnumerable<BlockingWebsiteItemViewModel> websites,
        IEnumerable<BlockingApplicationItemViewModel> applications,
        string websiteCountTemplate,
        string applicationCountTemplate,
        IFaviconService? faviconService = null,
        IEnumerable<RecentProgramRecord>? recentPrograms = null,
        AccessControlService? accessControlService = null,
        IProgramIconService? programIconService = null,
        IWebsiteMetadataService? websiteMetadataService = null)
    {
        Websites = new ObservableCollection<BlockingWebsiteItemViewModel>(websites);
        Applications = new ObservableCollection<BlockingApplicationItemViewModel>(applications);
        _websiteCountTemplate = websiteCountTemplate;
        _applicationCountTemplate = applicationCountTemplate;
        _faviconService = faviconService ?? new FaviconService();
        _websiteMetadataService = websiteMetadataService ?? new WebsiteMetadataService(_faviconService);
        _programIconService = programIconService ?? new ProgramIconService();
        _accessControlService = accessControlService ?? new AccessControlService();
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
        foreach (var website in Websites)
        {
            website.PropertyChanged += Website_PropertyChanged;
            _ = LoadFaviconAsync(website);
        }
        foreach (var application in Applications)
        {
            application.PropertyChanged += Application_PropertyChanged;
            _ = LoadProgramIconAsync(application);
        }
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

    /// <summary>
    /// Evaluates the current in-memory website rules for a UI or host caller.
    /// No browser, proxy, or notification side effect is performed here.
    /// </summary>
    public BlockedAccessResult EvaluateWebsiteAccess(string address)
        => _accessControlService.EvaluateWebsite(
            address,
            Websites.Select(item => new WebsiteAccessRule(item.Id, item.Name, item.Address, item.IsEnabled)));

    /// <summary>
    /// Evaluates the current in-memory application rules for a UI or host caller.
    /// </summary>
    public BlockedAccessResult EvaluateApplicationAccess(string path)
        => _accessControlService.EvaluateApplication(
            path,
            Applications.Select(item => new ApplicationAccessRule(item.Id, item.Name, item.Path, item.IsEnabled)));

    public BlockedAccessResult EvaluateAccess(AccessRequest request)
        => _accessControlService.Evaluate(
            request,
            Websites.Select(item => new WebsiteAccessRule(item.Id, item.Name, item.Address, item.IsEnabled)),
            Applications.Select(item => new ApplicationAccessRule(item.Id, item.Name, item.Path, item.IsEnabled)));

    public void ReplaceRules(
        IEnumerable<BlockingWebsiteItemViewModel> websites,
        IEnumerable<BlockingApplicationItemViewModel> applications)
    {
        ArgumentNullException.ThrowIfNull(websites);
        ArgumentNullException.ThrowIfNull(applications);
        foreach (var website in Websites)
        {
            website.PropertyChanged -= Website_PropertyChanged;
        }

        foreach (var application in Applications)
        {
            application.PropertyChanged -= Application_PropertyChanged;
        }

        Websites.Clear();
        Applications.Clear();
        foreach (var website in websites)
        {
            website.PropertyChanged += Website_PropertyChanged;
            Websites.Add(website);
            _ = LoadFaviconAsync(website);
        }

        foreach (var application in applications)
        {
            application.PropertyChanged += Application_PropertyChanged;
            Applications.Add(application);
            _ = LoadProgramIconAsync(application);
        }

        OnPropertyChanged(nameof(WebsiteCountText));
        OnPropertyChanged(nameof(ApplicationCountText));
        BlockingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WebsiteModal_WebsiteCreated(object? sender, WebsiteDraft draft)
    {
        var domain = AccessControlService.NormalizeWebsiteHost(draft.Address);
        if (domain is null)
        {
            return;
        }

        var note = draft.Name.Trim();
        var fallbackName = note.Length > 0 ? note : domain;
        var item = new BlockingWebsiteItemViewModel(Guid.NewGuid(), fallbackName, draft.Address, true);
        item.PropertyChanged += Website_PropertyChanged;
        Websites.Add(item);
        OnPropertyChanged(nameof(WebsiteCountText));
        BlockingChanged?.Invoke(this, EventArgs.Empty);
        _ = LoadWebsiteMetadataAsync(item, fallbackName, note.Length == 0);
    }

    private void WebsiteModal_WebsiteUpdated(object? sender, WebsiteEdit edit)
    {
        var item = Websites.FirstOrDefault(website => website.Id == edit.WebsiteId);
        if (item is null)
        {
            return;
        }

        var domain = AccessControlService.NormalizeWebsiteHost(edit.Draft.Address);
        if (domain is null)
        {
            return;
        }

        var note = edit.Draft.Name.Trim();
        var displayName = note.Length > 0 ? note : domain;
        var addressChanged = !string.Equals(item.Address, edit.Draft.Address, StringComparison.Ordinal);
        _isUpdatingWebsiteDetails = true;
        try
        {
            item.UpdateDetails(displayName, edit.Draft.Address);
        }
        finally
        {
            _isUpdatingWebsiteDetails = false;
        }
        if (addressChanged)
        {
            item.Favicon = null;
            _ = LoadWebsiteMetadataAsync(item, displayName, note.Length == 0);
        }
        else if (note.Length == 0)
        {
            _ = LoadWebsiteMetadataAsync(item, displayName, true);
        }

        BlockingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ProgramModal_ProgramSelected(object? sender, RecentProgramRecordViewModel program)
    {
        if (Applications.Any(application => AreSameExecutablePath(application.Path, program.ExePath)))
        {
            return;
        }

        var item = new BlockingApplicationItemViewModel(Guid.NewGuid(), program.DisplayName, program.ExePath, true);
        item.Icon = program.Icon;
        item.PropertyChanged += Application_PropertyChanged;
        Applications.Add(item);
        OnPropertyChanged(nameof(ApplicationCountText));
        BlockingChanged?.Invoke(this, EventArgs.Empty);
        if (item.Icon is null)
        {
            _ = LoadProgramIconAsync(item);
        }
    }

    private static bool AreSameExecutablePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return string.Equals(
                left.Replace('/', Path.DirectorySeparatorChar),
                right.Replace('/', Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task LoadProgramIconAsync(BlockingApplicationItemViewModel item)
    {
        try
        {
            var icon = await _programIconService.GetIconAsync(item.Path);
            if (icon is not null && Applications.Contains(item))
            {
                item.Icon = icon;
            }
        }
        catch
        {
            // The null icon keeps the default program glyph visible.
        }
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
        catch (Exception exception)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusApp", "Cache", "icon-cache-errors.log"), exception.ToString() + Environment.NewLine); } catch { }
        }
    }

    private async Task LoadWebsiteMetadataAsync(
        BlockingWebsiteItemViewModel item,
        string expectedFallbackName,
        bool canReplaceDisplayName)
    {
        var expectedAddress = item.Address;
        try
        {
            var metadata = await _websiteMetadataService.GetMetadataAsync(expectedAddress);
            if (!Websites.Contains(item) ||
                !string.Equals(item.Address, expectedAddress, StringComparison.Ordinal))
            {
                return;
            }

            if (metadata.Favicon is not null)
            {
                item.Favicon = metadata.Favicon;
            }

            if (canReplaceDisplayName &&
                !string.IsNullOrWhiteSpace(metadata.DisplayName) &&
                string.Equals(item.Name, expectedFallbackName, StringComparison.Ordinal))
            {
                item.SetDisplayName(metadata.DisplayName.Trim());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusApp", "Cache", "icon-cache-errors.log"), exception.ToString() + Environment.NewLine); } catch { }
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
        if (!_isUpdatingWebsiteDetails &&
            (e.PropertyName is nameof(BlockingWebsiteItemViewModel.IsEnabled) or
                nameof(BlockingWebsiteItemViewModel.Favicon) or
                nameof(BlockingWebsiteItemViewModel.Name)))
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

    public void SetDisplayName(string name)
    {
        if (_name == name)
        {
            return;
        }

        _name = name;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
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
    private ImageSource? _icon;

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

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
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
