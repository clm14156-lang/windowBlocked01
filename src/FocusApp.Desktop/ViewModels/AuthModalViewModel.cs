using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusApp.Desktop.ViewModels;

public sealed class AuthModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private bool _isRegistration;
    private string _loginAccount = string.Empty;
    private string _loginPassword = string.Empty;
    private string _registerEmail = string.Empty;
    private string _registerCode = string.Empty;
    private string _registerPassword = string.Empty;
    private string _registerConfirmPassword = string.Empty;
    private bool _hasLoginError;
    private bool _isPasswordRecovery;
    private PasswordRecoveryStep _passwordRecoveryStep = PasswordRecoveryStep.Account;
    private string _recoveryAccount = string.Empty;
    private string _recoveryCode = string.Empty;
    private string _recoveryNewPassword = string.Empty;
    private string _recoveryConfirmPassword = string.Empty;
    private int _resendSecondsRemaining;
    private readonly DispatcherTimer _resendTimer;

    public AuthModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        ShowLoginCommand = new RelayCommand<object>(_ => ShowLogin());
        ShowRegisterCommand = new RelayCommand<object>(_ => ShowRegister());
        LoginCommand = new RelayCommand<object>(_ => Login());
        StartPasswordRecoveryCommand = new RelayCommand<object>(_ => StartPasswordRecovery());
        PasswordRecoveryBackCommand = new RelayCommand<object>(_ => GoBackInPasswordRecovery());
        ContinueRecoveryCommand = new RelayCommand<object>(_ => ContinueRecovery());
        VerifyRecoveryCodeCommand = new RelayCommand<object>(_ => VerifyRecoveryCode());
        CompletePasswordRecoveryCommand = new RelayCommand<object>(_ => CompletePasswordRecovery());
        ReturnToLoginCommand = new RelayCommand<object>(_ => ReturnToLogin());
        ResendRecoveryCodeCommand = new RelayCommand<object>(_ => ResendRecoveryCode());

        _resendTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _resendTimer.Tick += (_, _) =>
        {
            if (ResendSecondsRemaining > 0)
            {
                ResendSecondsRemaining--;
            }

            if (ResendSecondsRemaining == 0)
            {
                _resendTimer.Stop();
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LoginSucceededEventArgs>? LoginSucceeded;

    public ICommand CloseCommand { get; }

    public ICommand ShowLoginCommand { get; }

    public ICommand ShowRegisterCommand { get; }

    public ICommand LoginCommand { get; }

    public ICommand StartPasswordRecoveryCommand { get; }

    public ICommand PasswordRecoveryBackCommand { get; }

    public ICommand ContinueRecoveryCommand { get; }

    public ICommand VerifyRecoveryCodeCommand { get; }

    public ICommand CompletePasswordRecoveryCommand { get; }

    public ICommand ReturnToLoginCommand { get; }

    public ICommand ResendRecoveryCodeCommand { get; }

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

    public bool IsRegistration
    {
        get => _isRegistration;
        private set
        {
            if (_isRegistration == value)
            {
                return;
            }

            _isRegistration = value;
            OnPropertyChanged();
        }
    }

    public bool IsPasswordRecovery
    {
        get => _isPasswordRecovery;
        private set => SetField(ref _isPasswordRecovery, value);
    }

    public PasswordRecoveryStep PasswordRecoveryStep
    {
        get => _passwordRecoveryStep;
        private set
        {
            if (SetField(ref _passwordRecoveryStep, value))
            {
                OnPropertyChanged(nameof(IsRecoveryAccountStep));
                OnPropertyChanged(nameof(IsRecoveryVerificationStep));
                OnPropertyChanged(nameof(IsRecoveryNewPasswordStep));
                OnPropertyChanged(nameof(IsRecoveryCompletedStep));
                OnPropertyChanged(nameof(IsRecoveryBackVisible));
            }
        }
    }

    public bool IsRecoveryAccountStep => PasswordRecoveryStep == PasswordRecoveryStep.Account;

    public bool IsRecoveryVerificationStep => PasswordRecoveryStep == PasswordRecoveryStep.Verification;

    public bool IsRecoveryNewPasswordStep => PasswordRecoveryStep == PasswordRecoveryStep.NewPassword;

    public bool IsRecoveryCompletedStep => PasswordRecoveryStep == PasswordRecoveryStep.Completed;

    public bool IsRecoveryBackVisible => !IsRecoveryCompletedStep;

    public string LoginAccount
    {
        get => _loginAccount;
        set
        {
            if (SetField(ref _loginAccount, value))
            {
                HasLoginError = false;
            }
        }
    }

    public string LoginPassword
    {
        get => _loginPassword;
        set
        {
            if (SetField(ref _loginPassword, value))
            {
                HasLoginError = false;
            }
        }
    }

    public string RegisterEmail
    {
        get => _registerEmail;
        set => SetField(ref _registerEmail, value);
    }

    public string RegisterCode
    {
        get => _registerCode;
        set => SetField(ref _registerCode, value);
    }

    public string RegisterPassword
    {
        get => _registerPassword;
        set => SetField(ref _registerPassword, value);
    }

    public string RegisterConfirmPassword
    {
        get => _registerConfirmPassword;
        set => SetField(ref _registerConfirmPassword, value);
    }

    public bool HasLoginError
    {
        get => _hasLoginError;
        private set => SetField(ref _hasLoginError, value);
    }

    public string RecoveryAccount
    {
        get => _recoveryAccount;
        set
        {
            if (SetField(ref _recoveryAccount, value))
            {
                OnPropertyChanged(nameof(CanContinueRecovery));
            }
        }
    }

    public string RecoveryCode
    {
        get => _recoveryCode;
        set
        {
            var normalized = new string((value ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());
            if (SetField(ref _recoveryCode, normalized))
            {
                OnPropertyChanged(nameof(CanVerifyRecoveryCode));
            }
        }
    }

    public string RecoveryNewPassword
    {
        get => _recoveryNewPassword;
        set
        {
            if (SetField(ref _recoveryNewPassword, value))
            {
                OnPropertyChanged(nameof(CanCompletePasswordRecovery));
            }
        }
    }

    public string RecoveryConfirmPassword
    {
        get => _recoveryConfirmPassword;
        set
        {
            if (SetField(ref _recoveryConfirmPassword, value))
            {
                OnPropertyChanged(nameof(CanCompletePasswordRecovery));
            }
        }
    }

    public int ResendSecondsRemaining
    {
        get => _resendSecondsRemaining;
        private set
        {
            if (SetField(ref _resendSecondsRemaining, value))
            {
                OnPropertyChanged(nameof(IsResendAvailable));
                OnPropertyChanged(nameof(ResendCountdownText));
            }
        }
    }

    public bool CanContinueRecovery => !string.IsNullOrWhiteSpace(RecoveryAccount);

    public bool CanVerifyRecoveryCode => RecoveryCode.Length == 6;

    public bool CanCompletePasswordRecovery =>
        !string.IsNullOrEmpty(RecoveryNewPassword) &&
        RecoveryNewPassword == RecoveryConfirmPassword;

    public bool IsResendAvailable => ResendSecondsRemaining == 0;

    public string ResendCountdownText => ResendSecondsRemaining > 0
        ? $"{ResendSecondsRemaining}s 后可重新发送"
        : "重新发送验证码";

    public void OpenLogin()
    {
        IsRegistration = false;
        IsPasswordRecovery = false;
        HasLoginError = false;
        IsOpen = true;
    }

    public void ClearSimulatedAccountData()
    {
        IsOpen = false;
        IsRegistration = false;
        IsPasswordRecovery = false;
        LoginAccount = string.Empty;
        LoginPassword = string.Empty;
        RegisterEmail = string.Empty;
        RegisterCode = string.Empty;
        RegisterPassword = string.Empty;
        RegisterConfirmPassword = string.Empty;
        ResetPasswordRecovery();
        HasLoginError = false;
    }

    private void Close()
    {
        _resendTimer.Stop();
        IsOpen = false;
    }

    private void ShowLogin()
    {
        IsRegistration = false;
        IsPasswordRecovery = false;
        _resendTimer.Stop();
    }

    private void ShowRegister()
    {
        IsPasswordRecovery = false;
        _resendTimer.Stop();
        IsRegistration = true;
    }

    private void StartPasswordRecovery()
    {
        IsRegistration = false;
        IsPasswordRecovery = true;
        PasswordRecoveryStep = PasswordRecoveryStep.Account;
        RecoveryCode = string.Empty;
        RecoveryNewPassword = string.Empty;
        RecoveryConfirmPassword = string.Empty;
        ResendSecondsRemaining = 0;
    }

    private void ContinueRecovery()
    {
        if (!CanContinueRecovery)
        {
            return;
        }

        PasswordRecoveryStep = PasswordRecoveryStep.Verification;
        RecoveryCode = string.Empty;
        StartResendCountdown();
    }

    private void VerifyRecoveryCode()
    {
        if (CanVerifyRecoveryCode)
        {
            _resendTimer.Stop();
            PasswordRecoveryStep = PasswordRecoveryStep.NewPassword;
        }
    }

    private void CompletePasswordRecovery()
    {
        if (CanCompletePasswordRecovery)
        {
            PasswordRecoveryStep = PasswordRecoveryStep.Completed;
        }
    }

    private void GoBackInPasswordRecovery()
    {
        switch (PasswordRecoveryStep)
        {
            case PasswordRecoveryStep.Account:
                ReturnToLogin();
                break;
            case PasswordRecoveryStep.Verification:
                _resendTimer.Stop();
                PasswordRecoveryStep = PasswordRecoveryStep.Account;
                break;
            case PasswordRecoveryStep.NewPassword:
                PasswordRecoveryStep = PasswordRecoveryStep.Verification;
                StartResendCountdown();
                break;
            case PasswordRecoveryStep.Completed:
                ReturnToLogin();
                break;
        }
    }

    private void ReturnToLogin()
    {
        _resendTimer.Stop();
        IsPasswordRecovery = false;
        IsRegistration = false;
        ResetPasswordRecovery();
    }

    private void ResendRecoveryCode()
    {
        if (IsResendAvailable)
        {
            RecoveryCode = string.Empty;
            StartResendCountdown();
        }
    }

    private void StartResendCountdown()
    {
        ResendSecondsRemaining = 55;
        _resendTimer.Stop();
        _resendTimer.Start();
    }

    private void ResetPasswordRecovery()
    {
        _resendTimer.Stop();
        PasswordRecoveryStep = PasswordRecoveryStep.Account;
        RecoveryAccount = string.Empty;
        RecoveryCode = string.Empty;
        RecoveryNewPassword = string.Empty;
        RecoveryConfirmPassword = string.Empty;
        ResendSecondsRemaining = 0;
    }

    private void Login()
    {
        var account = LoginAccount.Trim();
        var membershipType = (account, LoginPassword) switch
        {
            ("123", "123") => MembershipType.Normal,
            ("456", "456") => MembershipType.Annual,
            ("789", "789") => MembershipType.Lifetime,
            _ => (MembershipType?)null
        };

        if (membershipType is null)
        {
            HasLoginError = true;
            return;
        }

        HasLoginError = false;
        IsOpen = false;
        LoginSucceeded?.Invoke(this, new LoginSucceededEventArgs(account, membershipType.Value));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum PasswordRecoveryStep
{
    Account,
    Verification,
    NewPassword,
    Completed
}
