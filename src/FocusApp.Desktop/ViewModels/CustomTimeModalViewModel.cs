using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class CustomTimeModalViewModel : INotifyPropertyChanged
{
    private readonly Func<int, bool> _confirm;
    private readonly Action<int> _toggleVisibility;
    private readonly Action<int> _deleteTime;
    private bool _isOpen;
    private int _minutes;

    public CustomTimeModalViewModel(Func<int, bool> confirm, IEnumerable<HomeDurationOptionViewModel>? commonTimes = null, Action<int>? toggleVisibility = null, Action<int>? deleteTime = null)
    {
        _confirm = confirm;
        CommonTimes = new ObservableCollection<HomeDurationOptionViewModel>(commonTimes ?? []);
        _toggleVisibility = toggleVisibility ?? (_ => { });
        _deleteTime = deleteTime ?? (_ => { });
        CancelCommand = new RelayCommand<object>(_ => Close());
        ConfirmCommand = new RelayCommand<object>(_ => Confirm());
        ToggleEditCommand = new RelayCommand<object>(_ => IsEditing = !IsEditing);
        CompleteEditCommand = new RelayCommand<object>(_ => IsEditing = false);
        DeleteTimeCommand = new RelayCommand<HomeDurationOptionViewModel>(DeleteTime);
        SelectTimeCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectTime);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? InputResetRequested;

    public ICommand CancelCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand ToggleEditCommand { get; }
    public ICommand CompleteEditCommand { get; }
    public ICommand DeleteTimeCommand { get; }
    public ICommand SelectTimeCommand { get; }

    public ObservableCollection<HomeDurationOptionViewModel> CommonTimes { get; }

    public bool IsEditing
    {
        get => _isEditing;
        private set { if (_isEditing == value) return; _isEditing = value; OnPropertyChanged(); }
    }

    private bool _isEditing;

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
        IsEditing = false;
        IsOpen = true;
    }

    private void Close()
    {
        IsEditing = false;
        IsOpen = false;
    }

    private void Confirm()
    {
        if (Minutes is >= 1 and <= 999)
        {
            if (_confirm(Minutes))
            {
                Minutes = 0;
                InputResetRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void SelectTime(HomeDurationOptionViewModel? option)
    {
        if (option is null || IsEditing) return;
        _toggleVisibility(option.Minutes);
    }

    private void DeleteTime(HomeDurationOptionViewModel? option)
    {
        if (option is null) return;
        _deleteTime(option.Minutes);
        CommonTimes.Remove(option);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
