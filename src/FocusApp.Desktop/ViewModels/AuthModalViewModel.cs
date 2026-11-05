using System.ComponentModel;
using FocusApp.Desktop.Services.Cloud;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AuthModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _loginAccount = string.Empty;
    private string _loginPassword = string.Empty;
    private readonly IAccountClient _accounts;
    private bool _hasLoginError;
    private bool _isBusy;
    private string _statusMessage = "";
    private string _sessionStatus = "";
    private bool _initialSessionLoaded;

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
        CloseCommand = new RelayCommand<object>(_ => Close());
        LoginCommand = new AsyncCommand(() => RunAsync(LoginAsync));
        OpenRegistrationWebsiteCommand = new RelayCommand<object>(_ => OpenWebsite(AccountWebsiteLinks.Registration));
        OpenRecoveryWebsiteCommand = new RelayCommand<object>(_ => OpenWebsite(AccountWebsiteLinks.PasswordRecovery));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LoginSucceededEventArgs>? LoginSucceeded;

    public ICommand CloseCommand { get; }

    public ICommand LoginCommand { get; }
    public ICommand OpenRegistrationWebsiteCommand { get; }
    public ICommand OpenRecoveryWebsiteCommand { get; }

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

    public string LoginAccount
    {
        get => _loginAccount;
        set
        {
            if (SetField(ref _loginAccount, value) && HasLoginError) ResetFeedback();
        }
    }

    public string LoginPassword
    {
        get => _loginPassword;
        set
        {
            if (SetField(ref _loginPassword, value) && HasLoginError) ResetFeedback();
        }
    }

    public bool HasLoginError
    {
        get => _hasLoginError;
        private set => SetField(ref _hasLoginError, value);
    }

    public void OpenLogin()
    {
        if (!IsOpen) ResetFeedback();
        IsOpen = true;
    }

    public void ClearAccountForm()
    {
        ClearPasswords();
        IsOpen = false;
        LoginAccount = string.Empty;
        ResetFeedback();
    }

    private void Close()
    {
        if (IsBusy) return;
        ClearPasswords();
        ResetFeedback();
        IsOpen = false;
    }

    private void OpenWebsite(Uri? address)
    {
        if (IsBusy) return;
        ResetFeedback();
        if (address is null)
        {
            StatusMessage = "功能即将开放";
            return;
        }

        try { AccountWebsiteLinks.Open(address); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { StatusMessage = "无法打开浏览器，请稍后重试。"; }
    }

    private void ResetFeedback()
    {
        HasLoginError = false;
        StatusMessage = "";
    }

    private async Task LoginAsync()
    {
        var account = await _accounts.LoginAsync(LoginAccount.Trim(), LoginPassword);
        HasLoginError = false;
        ClearPasswords();
        IsOpen = false;
        LoginSucceeded?.Invoke(this, new LoginSucceededEventArgs(account.Email, account.Membership));
    }

    public async Task RestoreSessionAsync()
    {
        if (IsBusy) return;
        try
        {
            if (!_initialSessionLoaded && _accounts is CloudAccountClient cloud)
            {
                var cached = await cloud.ReadCachedAccountAsync();
                _initialSessionLoaded = true;
                if (cached is not null)
                {
                    SessionStatus = "正在验证已保存的登录状态";
                    LoginSucceeded?.Invoke(this, new LoginSucceededEventArgs(cached.Email, cached.Membership));
                }
                else SessionCleared?.Invoke(this, EventArgs.Empty);
            }
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
        HasLoginError = false;
        StatusMessage = "正在处理，请稍候…";
        try { await action(); if (StatusMessage == "正在处理，请稍候…") StatusMessage = ""; }
        catch (CloudApiException ex)
        {
            HasLoginError = true;
            StatusMessage = ex.Code switch
            {
                "invalid_credentials" => "邮箱或密码不正确。",
                "account_locked" => "登录失败次数过多，请在 15 分钟后重试。",
                "rate_limit_exceeded" => "操作过于频繁，请稍后再试。",
                "session_limit_reached" => "登录会话已达上限，请先退出已有设备或会话。",
                "authentication_required" or "invalid_session" or "refresh_reused" => "登录已失效，请重新登录。",
                "validation_failed" or "invalid_device" => "请检查邮箱和密码格式。",
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
        LoginPassword = "";
        PasswordsCleared?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SessionCleared;
    public event EventHandler? PasswordsCleared;
    public bool IsBusy { get => _isBusy; private set { SetField(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string SessionStatus { get => _sessionStatus; private set => SetField(ref _sessionStatus, value); }

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
