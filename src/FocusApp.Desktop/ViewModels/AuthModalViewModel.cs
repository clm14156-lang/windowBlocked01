using System.ComponentModel;
using FocusApp.Desktop.Services.Cloud;
using System.Net.Http;
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

    private readonly IAccountClient _accounts;
    private Guid _registerChallenge;
    private Guid _resetChallenge;
    private string _resetTicket = "";
    private bool _isBusy;
    private string _statusMessage = "";
    private bool _agreementAccepted;
    private string _sessionStatus = "";

    public AuthModalViewModel(IAccountClient? accounts = null)
    {
        _accounts = accounts ?? CloudAccountClient.CreateDefault();
        if (_accounts is IDeviceSessionClient devices)
            devices.SessionEnded += (_, _) =>
            {
                SessionStatus = "登录已失效";
                StatusMessage = "当前登录已退出或失效，请重新登录。";
                SessionCleared?.Invoke(this, EventArgs.Empty);
            };
        RegisterCommand = new AsyncCommand(() => RunAsync(RegisterAsync));
        SendRegisterCodeCommand = new AsyncCommand(() => RunAsync(SendRegisterCodeAsync));
        CloseCommand = new RelayCommand<object>(_ => Close());
        ShowLoginCommand = new RelayCommand<object>(_ => ShowLogin());
        ShowRegisterCommand = new RelayCommand<object>(_ => ShowRegister());
        LoginCommand = new AsyncCommand(() => RunAsync(LoginAsync));
        StartPasswordRecoveryCommand = new RelayCommand<object>(_ => StartPasswordRecovery());
        PasswordRecoveryBackCommand = new RelayCommand<object>(_ => GoBackInPasswordRecovery());
        ContinueRecoveryCommand = new AsyncCommand(() => RunAsync(ContinueRecoveryAsync));
        VerifyRecoveryCodeCommand = new AsyncCommand(() => RunAsync(VerifyRecoveryCodeAsync));
        CompletePasswordRecoveryCommand = new AsyncCommand(() => RunAsync(CompletePasswordRecoveryAsync));
        ReturnToLoginCommand = new RelayCommand<object>(_ => ReturnToLogin());
        ResendRecoveryCodeCommand = new AsyncCommand(() => RunAsync(ResendRecoveryCodeAsync));

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

    public void ClearAccountForm()
    {
        ClearPasswords();
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
        if (IsBusy) return;
        ClearPasswords();
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

    private async Task ContinueRecoveryAsync()
    {
        if (!CanContinueRecovery) return;
        var challenge = await _accounts.SendCodeAsync(RecoveryAccount.Trim(), true);
        _resetChallenge = challenge.ChallengeId;
        PasswordRecoveryStep = PasswordRecoveryStep.Verification;
        RecoveryCode = string.Empty;
        StartResendCountdown(challenge.ResendAfterSeconds);
        StatusMessage = "如果邮箱已注册，验证码已发送，请检查收件箱。";
    }

    private async Task VerifyRecoveryCodeAsync()
    {
        if (!CanVerifyRecoveryCode) return;
        var ticket = await _accounts.VerifyResetAsync(_resetChallenge, RecoveryCode);
        _resetTicket = ticket.ResetTicketValue;
        _resendTimer.Stop();
        PasswordRecoveryStep = PasswordRecoveryStep.NewPassword;
    }

    private async Task CompletePasswordRecoveryAsync()
    {
        if (!CanCompletePasswordRecovery) return;
        await _accounts.CompleteResetAsync(_resetTicket, RecoveryNewPassword);
        _resetTicket = "";
        ClearPasswords();
        PasswordRecoveryStep = PasswordRecoveryStep.Completed;
        await _accounts.LogoutAsync();
        SessionCleared?.Invoke(this, EventArgs.Empty);
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

    private async Task ResendRecoveryCodeAsync()
    {
        if (IsResendAvailable) await ContinueRecoveryAsync();
    }

    private void StartResendCountdown(int seconds = 60)
    {
        ResendSecondsRemaining = seconds;
        _resendTimer.Stop();
        _resendTimer.Start();
    }

    private void ResetPasswordRecovery()
    {
        _resetTicket = "";
        _resetChallenge = Guid.Empty;
        _resendTimer.Stop();
        PasswordRecoveryStep = PasswordRecoveryStep.Account;
        RecoveryAccount = string.Empty;
        RecoveryCode = string.Empty;
        RecoveryNewPassword = string.Empty;
        RecoveryConfirmPassword = string.Empty;
        ResendSecondsRemaining = 0;
    }

    private async Task LoginAsync()
    {
        var account = await _accounts.LoginAsync(LoginAccount.Trim(), LoginPassword);
        HasLoginError = false;
        ClearPasswords();
        IsOpen = false;
        LoginSucceeded?.Invoke(this, new LoginSucceededEventArgs(account.Email, account.Membership));
    }

    private async Task SendRegisterCodeAsync()
    {
        if (!IsResendAvailable) return;
        var challenge = await _accounts.SendCodeAsync(RegisterEmail.Trim(), false);
        _registerChallenge = challenge.ChallengeId;
        StartResendCountdown(challenge.ResendAfterSeconds);
        StatusMessage = "验证码已发送，请检查邮箱。";
    }

    private async Task RegisterAsync()
    {
        if (!AgreementAccepted) { StatusMessage = "请先阅读并同意用户协议和隐私政策。"; return; }
        if (RegisterPassword != RegisterConfirmPassword) { StatusMessage = "两次输入的密码不一致。"; return; }
        await _accounts.RegisterAsync(RegisterEmail.Trim(), _registerChallenge, RegisterCode, RegisterPassword);
        LoginAccount = RegisterEmail.Trim();
        ClearPasswords();
        RegisterCode = "";
        _registerChallenge = Guid.Empty;
        ShowLogin();
        StatusMessage = "注册成功，请使用邮箱和密码登录。";
    }

    public async Task RestoreSessionAsync()
    {
        if (IsBusy) return;
        try
        {
            var account = await _accounts.RestoreAsync();
            if (account is null) { SessionStatus = ""; SessionCleared?.Invoke(this, EventArgs.Empty); return; }
            SessionStatus = account.IsOffline ? "离线：暂时无法验证云端会话" : "云端会话已连接";
            LoginSucceeded?.Invoke(this, new LoginSucceededEventArgs(account.Email, account.Membership));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CloudApiException or
            System.IO.IOException or System.UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { SessionStatus = "暂时无法恢复云端会话，请稍后重试。"; }
    }

    public Task LogoutAsync() => RunAsync(async () =>
    {
        await _accounts.LogoutAsync();
        SessionCleared?.Invoke(this, EventArgs.Empty);
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在处理，请稍候…";
        try { await action(); if (StatusMessage == "正在处理，请稍候…") StatusMessage = ""; }
        catch (CloudApiException ex)
        {
            HasLoginError = true;
            StatusMessage = ex.Code switch
            {
                "invalid_credentials" => "邮箱或密码不正确。",
                "invalid_code" => "验证码无效、已使用或已过期，请重新获取。",
                "email_registered" => "该邮箱已经注册，请登录或找回密码。",
                "password_policy_failed" => "密码需要 8–128 个字符，并包含字母和数字。",
                "invalid_reset_ticket" => "重置凭证已过期，请重新找回密码。",
                "account_locked" => "登录失败次数过多，请在 15 分钟后重试。",
                "email_rate_limited" or "rate_limit_exceeded" => "操作过于频繁，请稍后再试。",
                "session_limit_reached" => "登录会话已达上限，请先退出已有设备或会话。",
                "authentication_required" or "invalid_session" or "refresh_reused" => "登录已失效，请重新登录。",
                "email_unavailable" => "邮件服务暂不可用，请稍后再试。",
                "validation_failed" or "invalid_device" => "请检查邮箱、验证码和密码格式。",
                _ => "请求未完成，请稍后重试。"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { HasLoginError = true; StatusMessage = "无法连接服务器，请检查网络后重试。"; }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { StatusMessage = "无法安全保存或读取账号状态，请稍后重试。"; }
        finally { IsBusy = false; }
    }

    private void ClearPasswords()
    {
        LoginPassword = RegisterPassword = RegisterConfirmPassword = RecoveryNewPassword = RecoveryConfirmPassword = "";
        PasswordsCleared?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SessionCleared;
    public event EventHandler? PasswordsCleared;
    public ICommand RegisterCommand { get; }
    public ICommand SendRegisterCodeCommand { get; }
    public bool IsBusy { get => _isBusy; private set { SetField(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string SessionStatus { get => _sessionStatus; private set => SetField(ref _sessionStatus, value); }
    public bool AgreementAccepted { get => _agreementAccepted; set => SetField(ref _agreementAccepted, value); }

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
