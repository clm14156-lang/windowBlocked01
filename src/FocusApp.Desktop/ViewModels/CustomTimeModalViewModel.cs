using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class CustomTimeModalViewModel : INotifyPropertyChanged
{
    private readonly Func<int, bool> _confirm;
    private readonly Action<int> _toggleVisibility;
    private readonly Action<int> _deleteTime;
    private readonly RelayCommand<object> _confirmCommand;
    private bool _isOpen;
    private string _minutesInput = "5";

    public CustomTimeModalViewModel(Func<int, bool> confirm, IEnumerable<HomeDurationOptionViewModel>? commonTimes = null, Action<int>? toggleVisibility = null, Action<int>? deleteTime = null)
    {
        _confirm = confirm;
        CommonTimes = new ObservableCollection<HomeDurationOptionViewModel>(commonTimes ?? []);
        _toggleVisibility = toggleVisibility ?? (_ => { });
        _deleteTime = deleteTime ?? (_ => { });
        CancelCommand = new RelayCommand<object>(_ => Close());
        _confirmCommand = new RelayCommand<object>(_ => Confirm(), _ => IsDurationValid);
        ConfirmCommand = _confirmCommand;
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
        get => int.TryParse(MinutesInput, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : 0;
        set => MinutesInput = value.ToString(CultureInfo.InvariantCulture);
    }

    public string MinutesInput
    {
        get => _minutesInput;
        set
        {
            value ??= string.Empty;
            if (_minutesInput == value) return;
            _minutesInput = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Minutes));
            OnPropertyChanged(nameof(IsDurationValid));
            _confirmCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsDurationValid => Minutes is >= 5 and <= 480;

    public void Open()
    {
        IsEditing = false;
        Minutes = 5;
        IsOpen = true;
    }

    private void Close()
    {
        IsEditing = false;
        IsOpen = false;
    }

    private void Confirm()
    {
        var minutes = Minutes;
        if (minutes is >= 5 and <= 480)
        {
            if (_confirm(minutes))
            {
                Minutes = 0;
                InputResetRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void SelectTime(HomeDurationOptionViewModel? option)
    {
        if (option is null || IsEditing) return;
        Minutes = option.Minutes;
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
