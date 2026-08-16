using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class CustomTimeModalViewModel : INotifyPropertyChanged
{
    private readonly Action<int> _confirm;
    private bool _isOpen;
    private int _minutes = 90;

    public CustomTimeModalViewModel(Action<int> confirm)
    {
        _confirm = confirm;
        CancelCommand = new RelayCommand<object>(_ => Close());
        ConfirmCommand = new RelayCommand<object>(_ => Confirm());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CancelCommand { get; }

    public ICommand ConfirmCommand { get; }

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

    public int Minutes
    {
        get => _minutes;
        set
        {
            if (_minutes == value)
            {
                return;
            }

            _minutes = value;
            OnPropertyChanged();
        }
    }

    public void Open()
    {
        IsOpen = true;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void Confirm()
    {
        _confirm(Minutes);
        Close();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
