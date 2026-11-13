using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Desktop.Services.Cloud;

namespace FocusApp.Desktop.ViewModels;

public sealed class AccountSyncModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _maskedEmail = string.Empty;
    private string _websiteStatus = string.Empty;

    public AccountSyncModalViewModel(FocusUploadCoordinator? uploads = null)
    {
        FocusUploads = uploads;
        if (uploads is not null)
        {
            uploads.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FocusUploadCoordinator.Presentation))
                    PropertyChanged?.Invoke(this, new(nameof(SyncPresentation)));
            };
        }

        CloseCommand = new RelayCommand<object>(_ => Close());
        OpenAccountWebsiteCommand = new RelayCommand<string>(OpenAccountWebsite);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FocusUploadCoordinator? FocusUploads { get; }
    public CloudSyncPresentation SyncPresentation => FocusUploads?.Presentation ?? CloudSyncPresentation.SignedOut;
    public ICommand CloseCommand { get; }
    public ICommand OpenAccountWebsiteCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public string MaskedEmail
    {
        get => _maskedEmail;
        private set => SetField(ref _maskedEmail, value);
    }

    public string WebsiteStatus
    {
        get => _websiteStatus;
        private set => SetField(ref _websiteStatus, value);
    }

    public void Open(string email)
    {
        var separator = email.LastIndexOf('@');
        MaskedEmail = separator > 0 && separator < email.Length - 1
            ? $"{email[0]}***{email[separator..]}"
            : "—";
        WebsiteStatus = string.Empty;
        IsOpen = true;
    }

    private void Close()
    {
        IsOpen = false;
        MaskedEmail = string.Empty;
        WebsiteStatus = string.Empty;
    }

    private void OpenAccountWebsite(string? action)
    {
        if (!IsOpen) return;

        var address = action switch
        {
            "ChangePassword" => AccountWebsiteLinks.ChangePassword,
            "ManageDevices" => AccountWebsiteLinks.ManageDevices,
            "DeleteAccount" => AccountWebsiteLinks.DeleteAccount,
            _ => null
        };

        if (address is null)
        {
            WebsiteStatus = "该功能将在官网开放，敬请期待";
            return;
        }

        try
        {
            AccountWebsiteLinks.Open(address);
            WebsiteStatus = string.Empty;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            WebsiteStatus = "暂时无法打开官网，请稍后重试。";
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;

        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
