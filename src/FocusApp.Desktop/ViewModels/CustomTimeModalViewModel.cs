using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class CustomTimeModalViewModel : INotifyPropertyChanged
{
    private readonly Func<int, bool> _confirm;
    private readonly Action<IReadOnlyList<int>, int> _saveSelection;
    private readonly Func<IEnumerable<HomeDurationOptionViewModel>> _savedSelection;
    private readonly List<int> _selectedMinutes = [];
    private readonly Action<int> _deleteTime;
    private readonly RelayCommand<object> _confirmCommand;
    private bool _isOpen;
    private string _minutesInput = "5";

    public CustomTimeModalViewModel(Func<int, bool> confirm, IEnumerable<HomeDurationOptionViewModel>? commonTimes = null,
        Action<IReadOnlyList<int>, int>? saveSelection = null, Action<int>? deleteTime = null,
        Func<IEnumerable<HomeDurationOptionViewModel>>? savedSelection = null)
    {
        _confirm = confirm;
        CommonTimes = new ObservableCollection<HomeDurationOptionViewModel>(commonTimes ?? []);
        CommonTimes.CollectionChanged += (_, _) => RefreshSelection();
        _saveSelection = saveSelection ?? ((_, _) => { });
        _savedSelection = savedSelection ?? (() => CommonTimes.Where(option => option.IsSelected));
        _deleteTime = deleteTime ?? (_ => { });
        CancelCommand = new RelayCommand<object>(_ => Close());
        _confirmCommand = new RelayCommand<object>(_ => Confirm(), _ => IsDurationValid);
        ConfirmCommand = _confirmCommand;
        ToggleEditCommand = new RelayCommand<object>(_ => IsEditing = !IsEditing);
        CompleteEditCommand = new RelayCommand<object>(_ => IsEditing = false);
        DeleteTimeCommand = new RelayCommand<HomeDurationOptionViewModel>(DeleteTime);
        SelectTimeCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectTime);
        RestoreSelection();
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

    public IReadOnlyList<int> SelectedMinutes => _selectedMinutes.AsReadOnly();

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
        RestoreSelection();
        Minutes = 5;
        IsOpen = true;
    }

    private void Close()
    {
        IsEditing = false;
        RestoreSelection();
        IsOpen = false;
    }

    private void Confirm()
    {
        var minutes = Minutes;
        if (minutes is < 5 or > 480) return;
        var isNewTime = CommonTimes.All(option => option.Minutes != minutes);
        if (isNewTime)
        {
            if (!_confirm(minutes)) return;
            AddSelection(minutes);
        }
        _saveSelection(_selectedMinutes.ToArray(), minutes);
        RefreshSelection();
        if (!isNewTime) return;
        Minutes = 0;
        InputResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SelectTime(HomeDurationOptionViewModel? option)
    {
        if (option is null || IsEditing || !CommonTimes.Contains(option)) return;
        Minutes = option.Minutes;
        if (!_selectedMinutes.Remove(option.Minutes)) AddSelection(option.Minutes);
        RefreshSelection();
    }

    private void DeleteTime(HomeDurationOptionViewModel? option)
    {
        if (option is null) return;
        _selectedMinutes.Remove(option.Minutes);
        _deleteTime(option.Minutes);
        CommonTimes.Remove(option);
        RefreshSelection();
    }

    private void AddSelection(int minutes)
    {
        if (_selectedMinutes.Contains(minutes)) return;
        if (_selectedMinutes.Count == 4) _selectedMinutes.RemoveAt(0);
        _selectedMinutes.Add(minutes);
    }

    private void RestoreSelection()
    {
        _selectedMinutes.Clear();
        _selectedMinutes.AddRange(_savedSelection().Where(option => CommonTimes.Contains(option))
            .Select(option => option.Minutes).Distinct().Take(4));
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        foreach (var option in CommonTimes)
            option.IsSelectedInCustomTime = _selectedMinutes.Contains(option.Minutes);
        OnPropertyChanged(nameof(SelectedMinutes));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
