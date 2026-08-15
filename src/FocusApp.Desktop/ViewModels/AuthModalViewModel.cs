using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AuthModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private bool _isRegistration;

    public AuthModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        ShowLoginCommand = new RelayCommand<object>(_ => ShowLogin());
        ShowRegisterCommand = new RelayCommand<object>(_ => ShowRegister());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand ShowLoginCommand { get; }

    public ICommand ShowRegisterCommand { get; }

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

    public void OpenLogin()
    {
        IsRegistration = false;
        IsOpen = true;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
