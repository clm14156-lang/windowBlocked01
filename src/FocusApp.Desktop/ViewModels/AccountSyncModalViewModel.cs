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
    private bool _isMacBookProLoggedIn = true;

    public AccountSyncModalViewModel()
    {
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
        OpenDeviceLogoutConfirmationCommand = new RelayCommand<object>(_ => OpenDeviceLogoutConfirmation());
        ReturnFromDeviceLogoutConfirmationCommand = new RelayCommand<object>(_ => ReturnFromDeviceLogoutConfirmation());
        ConfirmDeviceLogoutCommand = new RelayCommand<object>(_ => ConfirmDeviceLogout());
    }

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

    public bool IsMacBookProLoggedIn
    {
        get => _isMacBookProLoggedIn;
        private set => SetField(ref _isMacBookProLoggedIn, value);
    }

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

    private void OpenDeviceLogoutConfirmation()
    {
        if (!IsMacBookProLoggedIn)
        {
            return;
        }

        ResetPasswordChange();
        ResetAccountDeletion();
        SelectedSectionKey = DevicesSection;
        IsDeviceLogoutConfirmationActive = true;
    }

    private void ReturnFromDeviceLogoutConfirmation()
    {
        ResetDeviceLogoutConfirmation();
    }

    private void ConfirmDeviceLogout()
    {
        if (!IsDeviceLogoutConfirmationActive || !IsMacBookProLoggedIn)
        {
            return;
        }

        IsMacBookProLoggedIn = false;
        ResetDeviceLogoutConfirmation();
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
