using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AddWebsiteModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private Guid? _editingWebsiteId;
    private string _websiteName = string.Empty;
    private string _websiteAddress = string.Empty;

    public AddWebsiteModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SaveCommand = new RelayCommand<object>(_ => Save());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<WebsiteDraft>? WebsiteCreated;

    public event EventHandler<WebsiteEdit>? WebsiteUpdated;

    public ICommand CloseCommand { get; }

    public ICommand SaveCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen == value)
            {
                return;
            }

            _isOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditMode => _editingWebsiteId.HasValue;

    public string WebsiteName
    {
        get => _websiteName;
        set
        {
            if (_websiteName == value)
            {
                return;
            }

            _websiteName = value;
            OnPropertyChanged();
        }
    }

    public string WebsiteAddress
    {
        get => _websiteAddress;
        set
        {
            if (_websiteAddress == value)
            {
                return;
            }

            _websiteAddress = value;
            OnPropertyChanged();
        }
    }

    public void Open()
    {
        _editingWebsiteId = null;
        OnPropertyChanged(nameof(IsEditMode));
        WebsiteName = string.Empty;
        WebsiteAddress = string.Empty;
        IsOpen = true;
    }

    public void OpenForEdit(Guid websiteId, string name, string address)
    {
        _editingWebsiteId = websiteId;
        OnPropertyChanged(nameof(IsEditMode));
        WebsiteName = name;
        WebsiteAddress = address;
        IsOpen = true;
    }

    private void Save()
    {
        var name = WebsiteName.Trim();
        var address = WebsiteAddress.Trim();
        if (name.Length == 0 || !IsValidWebsiteAddress(address))
        {
            return;
        }

        var draft = new WebsiteDraft(name, address);
        if (_editingWebsiteId is Guid websiteId)
        {
            WebsiteUpdated?.Invoke(this, new WebsiteEdit(websiteId, draft));
        }
        else
        {
            WebsiteCreated?.Invoke(this, draft);
        }

        Close();
    }

    private static bool IsValidWebsiteAddress(string address)
    {
        var value = address.Contains("://", StringComparison.Ordinal)
            ? address
            : $"https://{address}";

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && Uri.CheckHostName(uri.Host) != UriHostNameType.Unknown;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record WebsiteDraft(string Name, string Address);

public sealed record WebsiteEdit(Guid WebsiteId, WebsiteDraft Draft);
