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

    private bool _isOpen;
    private string _selectedSectionKey = CloudSection;
    private AccountPasswordChangeStage _passwordChangeStage;
    private string _verificationCode = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;

    public AccountSyncModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        NavigateSectionCommand = new RelayCommand<string>(SelectSection);
        OpenPasswordChangeCommand = new RelayCommand<object>(_ => OpenPasswordChange());
        ReturnFromPasswordChangeCommand = new RelayCommand<object>(_ => ReturnFromPasswordChange());
        ContinuePasswordChangeCommand = new RelayCommand<object>(_ => ContinuePasswordChange());
        SavePasswordCommand = new RelayCommand<object>(_ => SavePassword());
        CompletePasswordChangeCommand = new RelayCommand<object>(_ => ReturnFromPasswordChange());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand NavigateSectionCommand { get; }

    public ICommand OpenPasswordChangeCommand { get; }

    public ICommand ReturnFromPasswordChangeCommand { get; }

    public ICommand ContinuePasswordChangeCommand { get; }

    public ICommand SavePasswordCommand { get; }

    public ICommand CompletePasswordChangeCommand { get; }

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
        }
    }

    public bool IsPasswordChangeActive => PasswordChangeStage != AccountPasswordChangeStage.None;

    public bool IsVerificationStage => PasswordChangeStage == AccountPasswordChangeStage.VerifyIdentity;

    public bool IsNewPasswordStage => PasswordChangeStage == AccountPasswordChangeStage.SetNewPassword;

    public bool IsPasswordChangeSuccessStage => PasswordChangeStage == AccountPasswordChangeStage.Success;

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
        ResetPasswordChange();
        SelectedSectionKey = CloudSection;
        IsOpen = true;
    }

    public void SelectSection(string? sectionKey)
    {
        if (sectionKey is CloudSection or DevicesSection or SecuritySection)
        {
            ResetPasswordChange();
            SelectedSectionKey = sectionKey;
        }
    }

    private void OpenPasswordChange()
    {
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

    private void Close()
    {
        ResetPasswordChange();
        IsOpen = false;
    }

    private void ResetPasswordChange()
    {
        PasswordChangeStage = AccountPasswordChangeStage.None;
        VerificationCode = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
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
