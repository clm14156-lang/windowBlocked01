using System.Collections.ObjectModel;
using System.Net.Http;
using FocusApp.Desktop.Services.Cloud;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public enum AccountPasswordChangeStage
{
    None,
    VerifyIdentity,
    SetNewPassword,
    Success
}

public sealed class AccountSyncModalViewModel : INotifyPropertyChanged
{
    public const string CloudSection = "Cloud";
    public const string DevicesSection = "Devices";
    public const string SecuritySection = "Security";
    public const string AccountDeletionConfirmationPhrase = "永久注销";

    private bool _isOpen;
    private string _selectedSectionKey = CloudSection;
    private AccountPasswordChangeStage _passwordChangeStage;
    private string _verificationCode = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _isAccountDeletionActive;
    private string _accountDeletionConfirmation = string.Empty;
    private bool _isDeviceLogoutConfirmationActive;
    private readonly IDeviceSessionClient? _devicesClient;
    private bool _isBusy;
    private string _deviceStatus = "";
    private CloudDevice? _pendingDevice;
    private CloudSession? _pendingSession;
    private bool _pendingAll;
    private int _deviceGeneration;

    public AccountSyncModalViewModel(IDeviceSessionClient? devicesClient = null, FocusUploadCoordinator? uploads = null)
    {
        _devicesClient = devicesClient;
        FocusUploads=uploads;
        if (_devicesClient is not null) _devicesClient.SessionEnded += (_, _) => { ClearDevices(); Close(); };
        RefreshDevicesCommand = new AsyncCommand(() => RunDevicesAsync(RefreshDevicesCoreAsync));
        OpenSessionLogoutConfirmationCommand = new RelayCommand<CloudSession>(OpenSessionLogoutConfirmation);
        OpenLogoutAllConfirmationCommand = new RelayCommand<object>(_ => OpenLogoutAllConfirmation());
        CloseCommand = new RelayCommand<object>(_ => Close());
        NavigateSectionCommand = new RelayCommand<string>(SelectSection);
        OpenPasswordChangeCommand = new RelayCommand<object>(_ => OpenPasswordChange());
        ReturnFromPasswordChangeCommand = new RelayCommand<object>(_ => ReturnFromPasswordChange());
        ContinuePasswordChangeCommand = new RelayCommand<object>(_ => ContinuePasswordChange());
        SavePasswordCommand = new RelayCommand<object>(_ => SavePassword());
        CompletePasswordChangeCommand = new RelayCommand<object>(_ => ReturnFromPasswordChange());
        OpenAccountDeletionCommand = new RelayCommand<object>(_ => OpenAccountDeletion());
        ReturnFromAccountDeletionCommand = new RelayCommand<object>(_ => ReturnFromAccountDeletion());
        ConfirmAccountDeletionCommand = new RelayCommand<object>(_ => ConfirmAccountDeletion());
        OpenDeviceLogoutConfirmationCommand = new RelayCommand<CloudDevice>(OpenDeviceLogoutConfirmation);
        ReturnFromDeviceLogoutConfirmationCommand = new RelayCommand<object>(_ => ReturnFromDeviceLogoutConfirmation());
        ConfirmDeviceLogoutCommand = new AsyncCommand(() => RunDevicesAsync(ConfirmDeviceLogoutAsync));
    }

    public FocusUploadCoordinator? FocusUploads { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? AccountDeletionConfirmed;

    public ICommand CloseCommand { get; }

    public ICommand NavigateSectionCommand { get; }

    public ICommand OpenPasswordChangeCommand { get; }

    public ICommand ReturnFromPasswordChangeCommand { get; }

    public ICommand ContinuePasswordChangeCommand { get; }

    public ICommand SavePasswordCommand { get; }

    public ICommand CompletePasswordChangeCommand { get; }

    public ICommand OpenAccountDeletionCommand { get; }

    public ICommand ReturnFromAccountDeletionCommand { get; }

    public ICommand ConfirmAccountDeletionCommand { get; }

    public ICommand OpenDeviceLogoutConfirmationCommand { get; }

    public ICommand ReturnFromDeviceLogoutConfirmationCommand { get; }

    public ICommand ConfirmDeviceLogoutCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public string SelectedSectionKey
    {
        get => _selectedSectionKey;
        private set => SetField(ref _selectedSectionKey, value);
    }

    public AccountPasswordChangeStage PasswordChangeStage
    {
        get => _passwordChangeStage;
        private set
        {
            if (!SetField(ref _passwordChangeStage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsPasswordChangeActive));
            OnPropertyChanged(nameof(IsVerificationStage));
            OnPropertyChanged(nameof(IsNewPasswordStage));
            OnPropertyChanged(nameof(IsPasswordChangeSuccessStage));
            OnPropertyChanged(nameof(IsTransientContentActive));
        }
    }

    public bool IsPasswordChangeActive => PasswordChangeStage != AccountPasswordChangeStage.None;

    public bool IsVerificationStage => PasswordChangeStage == AccountPasswordChangeStage.VerifyIdentity;

    public bool IsNewPasswordStage => PasswordChangeStage == AccountPasswordChangeStage.SetNewPassword;

    public bool IsPasswordChangeSuccessStage => PasswordChangeStage == AccountPasswordChangeStage.Success;

    public bool IsAccountDeletionActive
    {
        get => _isAccountDeletionActive;
        private set
        {
            if (SetField(ref _isAccountDeletionActive, value))
            {
                OnPropertyChanged(nameof(IsTransientContentActive));
            }
        }
    }

    public bool IsDeviceLogoutConfirmationActive
    {
        get => _isDeviceLogoutConfirmationActive;
        private set
        {
            if (SetField(ref _isDeviceLogoutConfirmationActive, value))
            {
                OnPropertyChanged(nameof(IsTransientContentActive));
            }
        }
    }

    public ObservableCollection<CloudDevice> Devices { get; } = [];
    public ICommand RefreshDevicesCommand { get; }
    public ICommand OpenSessionLogoutConfirmationCommand { get; }
    public ICommand OpenLogoutAllConfirmationCommand { get; }
    public bool IsBusy { get => _isBusy; private set { SetField(ref _isBusy, value); OnPropertyChanged(nameof(IsDeviceIdle)); OnPropertyChanged(nameof(HasNoDevices)); } }
    public bool IsDeviceIdle => !IsBusy;
    public bool HasNoDevices => !IsBusy && Devices.Count == 0;
    public string DevicesCount => $"{Devices.Count} 台已登录设备";
    public string DeviceStatus { get => _deviceStatus; private set => SetField(ref _deviceStatus, value); }
    public string PendingLogoutName => _pendingAll ? "全部设备" : _pendingDevice?.Name ?? _pendingSession?.Label ?? "";
    public string PendingLogoutQuestion => _pendingAll ? "退出全部设备？" : _pendingDevice is not null ? "退出所选设备？" : "退出所选会话？";
    public string PendingLogoutDescription => _pendingAll ? "所有设备（包括当前设备）都需要重新登录。专注记录会保留。"
        : _pendingDevice is not null ? "该设备的所有登录会话将退出。专注记录会保留。"
        : "此登录会话将退出，其他会话保持有效。专注记录会保留。";

    public bool IsTransientContentActive =>
        IsPasswordChangeActive || IsAccountDeletionActive || IsDeviceLogoutConfirmationActive;

    public string AccountDeletionConfirmation
    {
        get => _accountDeletionConfirmation;
        set
        {
            if (SetField(ref _accountDeletionConfirmation, value))
            {
                OnPropertyChanged(nameof(CanConfirmAccountDeletion));
            }
        }
    }

    public bool CanConfirmAccountDeletion =>
        string.Equals(AccountDeletionConfirmation, AccountDeletionConfirmationPhrase, StringComparison.Ordinal);

    public string VerificationCode
    {
        get => _verificationCode;
        set
        {
            if (SetField(ref _verificationCode, value))
            {
                OnPropertyChanged(nameof(CanContinuePasswordChange));
            }
        }
    }

    public bool CanContinuePasswordChange => !string.IsNullOrWhiteSpace(VerificationCode);

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetField(ref _newPassword, value))
            {
                NotifyPasswordValidationChanged();
            }
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetField(ref _confirmPassword, value))
            {
                OnPropertyChanged(nameof(CanSavePassword));
            }
        }
    }

    public int PasswordStrengthLevel
    {
        get
        {
            if (NewPassword.Length == 0)
            {
                return 0;
            }

            var hasLetter = NewPassword.Any(char.IsLetter);
            var hasDigit = NewPassword.Any(char.IsDigit);
            if (NewPassword.Length < 8 || !hasLetter || !hasDigit)
            {
                return 1;
            }

            return NewPassword.Length >= 12 && NewPassword.Any(ch => !char.IsLetterOrDigit(ch)) ? 3 : 2;
        }
    }

    public string PasswordStrengthText => PasswordStrengthLevel switch
    {
        3 => "强",
        2 => "中等",
        1 => "弱",
        _ => "未输入"
    };

    public bool IsPasswordStrengthOneActive => PasswordStrengthLevel >= 1;

    public bool IsPasswordStrengthTwoActive => PasswordStrengthLevel >= 2;

    public bool IsPasswordStrengthThreeActive => PasswordStrengthLevel >= 3;

    public bool CanSavePassword =>
        NewPassword.Length >= 8 &&
        NewPassword.Any(char.IsLetter) &&
        NewPassword.Any(char.IsDigit) &&
        NewPassword == ConfirmPassword;

    public void Open()
    {
        ResetTransientContent();
        SelectedSectionKey = CloudSection;
        IsOpen = true;
        RefreshDevicesCommand.Execute(null);
    }

    public void SelectSection(string? sectionKey)
    {
        if (sectionKey is CloudSection or DevicesSection or SecuritySection)
        {
            ResetTransientContent();
            SelectedSectionKey = sectionKey;
        }
    }

    private void OpenPasswordChange()
    {
        ResetAccountDeletion();
        ResetDeviceLogoutConfirmation();
        SelectedSectionKey = SecuritySection;
        VerificationCode = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        PasswordChangeStage = AccountPasswordChangeStage.VerifyIdentity;
    }

    private void ReturnFromPasswordChange()
    {
        ResetPasswordChange();
    }

    private void ContinuePasswordChange()
    {
        if (CanContinuePasswordChange)
        {
            PasswordChangeStage = AccountPasswordChangeStage.SetNewPassword;
        }
    }

    private void SavePassword()
    {
        if (CanSavePassword)
        {
            PasswordChangeStage = AccountPasswordChangeStage.Success;
        }
    }

    private void OpenAccountDeletion()
    {
        ResetPasswordChange();
        ResetDeviceLogoutConfirmation();
        SelectedSectionKey = SecuritySection;
        AccountDeletionConfirmation = string.Empty;
        IsAccountDeletionActive = true;
    }

    private void ReturnFromAccountDeletion()
    {
        ResetAccountDeletion();
    }

    private void ConfirmAccountDeletion()
    {
        if (!CanConfirmAccountDeletion)
        {
            return;
        }

        Close();
        AccountDeletionConfirmed?.Invoke(this, EventArgs.Empty);
    }

    private void OpenDeviceLogoutConfirmation(CloudDevice? device)
    {
        if (device is null || IsBusy) return;
        ResetTransientContent();
        _pendingDevice = device;
        ShowDeviceConfirmation();
    }
    private void OpenSessionLogoutConfirmation(CloudSession? session)
    {
        if (session is null || IsBusy) return;
        ResetTransientContent();
        _pendingSession = session;
        ShowDeviceConfirmation();
    }
    private void OpenLogoutAllConfirmation()
    {
        if (IsBusy || Devices.Count == 0) return;
        ResetTransientContent();
        _pendingAll = true;
        ShowDeviceConfirmation();
    }
    private void ShowDeviceConfirmation()
    {
        SelectedSectionKey = DevicesSection;
        foreach (var name in new[] { nameof(PendingLogoutName), nameof(PendingLogoutQuestion), nameof(PendingLogoutDescription) }) OnPropertyChanged(name);
        IsDeviceLogoutConfirmationActive = true;
    }
    private void ReturnFromDeviceLogoutConfirmation()
    {
        if (!IsBusy) ResetDeviceLogoutConfirmation();
    }
    private async Task ConfirmDeviceLogoutAsync()
    {
        if (!IsDeviceLogoutConfirmationActive) return;
        var client = _devicesClient ?? throw new CloudApiException("authentication_required");
        if (_pendingAll) await client.LogoutAllAsync();
        else if (_pendingDevice is { } device) await client.RevokeDeviceAsync(device.Id);
        else if (_pendingSession is { } session) await client.RevokeSessionAsync(session.Id);
        else return;
        ResetDeviceLogoutConfirmation();
        if (IsOpen) { await RefreshDevicesCoreAsync(); DeviceStatus = "已退出选定的登录会话。"; }
    }
    private async Task RefreshDevicesCoreAsync()
    {
        var generation = _deviceGeneration;
        var items = await (_devicesClient ?? throw new CloudApiException("authentication_required")).GetDevicesAsync();
        if (generation != _deviceGeneration) return;
        Devices.Clear();
        foreach (var item in items) Devices.Add(item);
        NotifyDevicesChanged();
        DeviceStatus = "设备列表已更新。";
    }
    private async Task RunDevicesAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        DeviceStatus = "正在连接服务器…";
        try { await action(); }
        catch (CloudApiException error)
        {
            DeviceStatus = error.Code switch
            {
                "authentication_required" or "invalid_session" or "refresh_reused" => "登录已失效，请重新登录。",
                "not_found" => "该设备或会话已不存在，请刷新列表。",
                "rate_limit_exceeded" => "操作过于频繁，请稍后重试。",
                _ => "操作未完成，请稍后重试。"
            };
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        { DeviceStatus = "无法连接服务器，退出操作尚未确认，请联网后重试。"; }
        catch (Exception error) when (error is System.IO.IOException or System.UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { DeviceStatus = "无法读取账号状态或服务器响应，请稍后重试。"; }
        finally { IsBusy = false; }
    }
    public void ClearDevices()
    {
        _deviceGeneration++;
        DeviceStatus = "";
        Devices.Clear();
        ResetDeviceLogoutConfirmation();
        NotifyDevicesChanged();
    }
    private void NotifyDevicesChanged()
    {
        OnPropertyChanged(nameof(DevicesCount));
        OnPropertyChanged(nameof(HasNoDevices));
    }

    private void Close()
    {
        ResetTransientContent();
        IsOpen = false;
    }

    private void ResetTransientContent()
    {
        ResetPasswordChange();
        ResetAccountDeletion();
        ResetDeviceLogoutConfirmation();
    }

    private void ResetPasswordChange()
    {
        PasswordChangeStage = AccountPasswordChangeStage.None;
        VerificationCode = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    private void ResetAccountDeletion()
    {
        IsAccountDeletionActive = false;
        AccountDeletionConfirmation = string.Empty;
    }

    private void ResetDeviceLogoutConfirmation()
    {
        IsDeviceLogoutConfirmationActive = false;
        _pendingDevice = null;
        _pendingSession = null;
        _pendingAll = false;
    }

    private void NotifyPasswordValidationChanged()
    {
        OnPropertyChanged(nameof(PasswordStrengthLevel));
        OnPropertyChanged(nameof(PasswordStrengthText));
        OnPropertyChanged(nameof(IsPasswordStrengthOneActive));
        OnPropertyChanged(nameof(IsPasswordStrengthTwoActive));
        OnPropertyChanged(nameof(IsPasswordStrengthThreeActive));
        OnPropertyChanged(nameof(CanSavePassword));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
