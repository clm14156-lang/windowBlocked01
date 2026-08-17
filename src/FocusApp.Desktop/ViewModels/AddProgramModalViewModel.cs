using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AddProgramModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _searchText = string.Empty;
    private bool _wasChooseProgramPressed;

    public AddProgramModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SaveCommand = new RelayCommand<object>(_ => Close());
        ChooseProgramCommand = new RelayCommand<object>(_ => WasChooseProgramPressed = true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ChooseProgramCommand { get; }

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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
        }
    }

    public bool WasChooseProgramPressed
    {
        get => _wasChooseProgramPressed;
        private set
        {
            if (_wasChooseProgramPressed == value)
            {
                return;
            }

            _wasChooseProgramPressed = value;
            OnPropertyChanged();
        }
    }

    public void Open()
    {
        SearchText = string.Empty;
        WasChooseProgramPressed = false;
        IsOpen = true;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
