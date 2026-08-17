using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AddWebsiteModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _websiteName = string.Empty;
    private string _websiteAddress = string.Empty;

    public AddWebsiteModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SaveCommand = new RelayCommand<object>(_ => Save());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<WebsiteDraft>? WebsiteCreated;

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
        WebsiteName = string.Empty;
        WebsiteAddress = string.Empty;
        IsOpen = true;
    }

    private void Save()
    {
        var name = WebsiteName.Trim();
        var address = WebsiteAddress.Trim();
        if (name.Length == 0 || address.Length == 0)
        {
            return;
        }

        WebsiteCreated?.Invoke(this, new WebsiteDraft(name, address));
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
