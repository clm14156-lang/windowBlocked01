using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class AddWebsiteModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private Guid? _editingWebsiteId;
    private string _websiteName = string.Empty;
    private string _websiteAddress = string.Empty;
    private bool _isWebsiteNameExpanded;
    private readonly RelayCommand<object> _saveCommand;

    public AddWebsiteModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        _saveCommand = new RelayCommand<object>(_ => Save(), _ => CanSave);
        SaveCommand = _saveCommand;
        ToggleWebsiteNameCommand = new RelayCommand<object>(_ => IsWebsiteNameExpanded = !IsWebsiteNameExpanded);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<WebsiteDraft>? WebsiteCreated;

    public event EventHandler<WebsiteEdit>? WebsiteUpdated;

    public event EventHandler? WebsiteAddressValidationFailed;

    public ICommand CloseCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ToggleWebsiteNameCommand { get; }

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

    public bool CanSave => !string.IsNullOrWhiteSpace(WebsiteAddress);

    public bool IsWebsiteNameExpanded
    {
        get => _isWebsiteNameExpanded;
        private set
        {
            if (_isWebsiteNameExpanded == value)
            {
                return;
            }

            _isWebsiteNameExpanded = value;
            OnPropertyChanged();
        }
    }

    public string WebsiteName
    {
        get => _websiteName;
        set
        {
            value ??= string.Empty;
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
            value ??= string.Empty;
            if (_websiteAddress == value)
            {
                return;
            }

            _websiteAddress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
            _saveCommand.NotifyCanExecuteChanged();
        }
    }

    public void Open()
    {
        _editingWebsiteId = null;
        OnPropertyChanged(nameof(IsEditMode));
        WebsiteName = string.Empty;
        WebsiteAddress = string.Empty;
        IsWebsiteNameExpanded = false;
        IsOpen = true;
    }

    public void OpenForEdit(Guid websiteId, string name, string address)
    {
        _editingWebsiteId = websiteId;
        OnPropertyChanged(nameof(IsEditMode));
        WebsiteName = name;
        WebsiteAddress = address;
        IsWebsiteNameExpanded = true;
        IsOpen = true;
    }

    private void Save()
    {
        var name = WebsiteName.Trim();
        var address = WebsiteAddress.Trim();
        if (!CanSave)
        {
            return;
        }

        var normalizedDomain = AccessControlService.NormalizeWebsiteInput(address);
        if (!AccessControlService.IsValidDomain(normalizedDomain))
        {
            WebsiteAddressValidationFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var draft = new WebsiteDraft(name, normalizedDomain!);
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
