using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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

    public AuthModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        ShowLoginCommand = new RelayCommand<object>(_ => ShowLogin());
        ShowRegisterCommand = new RelayCommand<object>(_ => ShowRegister());
        LoginCommand = new RelayCommand<object>(_ => Login());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LoginSucceededEventArgs>? LoginSucceeded;

    public ICommand CloseCommand { get; }

    public ICommand ShowLoginCommand { get; }

    public ICommand ShowRegisterCommand { get; }

    public ICommand LoginCommand { get; }

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

    public void OpenLogin()
    {
        IsRegistration = false;
        HasLoginError = false;
        IsOpen = true;
    }

    public void ClearSimulatedAccountData()
    {
        IsOpen = false;
        IsRegistration = false;
        LoginAccount = string.Empty;
        LoginPassword = string.Empty;
        RegisterEmail = string.Empty;
        RegisterCode = string.Empty;
        RegisterPassword = string.Empty;
        RegisterConfirmPassword = string.Empty;
        HasLoginError = false;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void ShowLogin()
    {
        IsRegistration = false;
    }

    private void ShowRegister()
    {
        IsRegistration = true;
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
