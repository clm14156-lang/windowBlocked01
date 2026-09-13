using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public enum FocusGoalMode
{
    DailyFixed,
    MonthlyTotal
}

public enum FocusGoalRepeatMode
{
    EveryDay,
    Custom
}

public sealed class FocusGoalWeekdayOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public FocusGoalWeekdayOptionViewModel(string label, bool isSelected)
    {
        Label = label;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed class FocusGoalSettingsModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private FocusGoalMode _mode = FocusGoalMode.DailyFixed;
    private FocusGoalRepeatMode _repeatMode = FocusGoalRepeatMode.EveryDay;
    private int _dailyTargetHours = 4;
    private int _monthlyTargetHours = 60;

    public FocusGoalSettingsModalViewModel()
    {
        Weekdays = new ObservableCollection<FocusGoalWeekdayOptionViewModel>
        {
            new("一", true),
            new("二", true),
            new("三", true),
            new("四", true),
            new("五", true),
            new("六", false),
            new("日", false)
        };

        OpenCommand = new RelayCommand<object>(_ => Open());
        CancelCommand = new RelayCommand<object>(_ => Close());
        SaveCommand = new RelayCommand<object>(_ => Close());
        SelectDailyModeCommand = new RelayCommand<object>(_ => Mode = FocusGoalMode.DailyFixed);
        SelectMonthlyModeCommand = new RelayCommand<object>(_ => Mode = FocusGoalMode.MonthlyTotal);
        SelectEveryDayCommand = new RelayCommand<object>(_ => RepeatMode = FocusGoalRepeatMode.EveryDay);
        SelectCustomRepeatCommand = new RelayCommand<object>(_ => RepeatMode = FocusGoalRepeatMode.Custom);
        ToggleWeekdayCommand = new RelayCommand<FocusGoalWeekdayOptionViewModel>(ToggleWeekday);
        IncreaseDailyTargetCommand = new RelayCommand<object>(_ => DailyTargetHours = Math.Min(10000, DailyTargetHours + 1));
        DecreaseDailyTargetCommand = new RelayCommand<object>(_ => DailyTargetHours = Math.Max(1, DailyTargetHours - 1));
        IncreaseMonthlyTargetCommand = new RelayCommand<object>(_ => MonthlyTargetHours = Math.Min(10000, MonthlyTargetHours + 1));
        DecreaseMonthlyTargetCommand = new RelayCommand<object>(_ => MonthlyTargetHours = Math.Max(1, MonthlyTargetHours - 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FocusGoalWeekdayOptionViewModel> Weekdays { get; }

    public ICommand OpenCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand SelectDailyModeCommand { get; }

    public ICommand SelectMonthlyModeCommand { get; }

    public ICommand SelectEveryDayCommand { get; }

    public ICommand SelectCustomRepeatCommand { get; }

    public ICommand ToggleWeekdayCommand { get; }

    public ICommand IncreaseDailyTargetCommand { get; }

    public ICommand DecreaseDailyTargetCommand { get; }

    public ICommand IncreaseMonthlyTargetCommand { get; }

    public ICommand DecreaseMonthlyTargetCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public FocusGoalMode Mode
    {
        get => _mode;
        private set
        {
            if (!SetField(ref _mode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDailyFixedMode));
            OnPropertyChanged(nameof(IsMonthlyTotalMode));
            OnPropertyChanged(nameof(DialogHeight));
        }
    }

    public bool IsDailyFixedMode => Mode == FocusGoalMode.DailyFixed;

    public bool IsMonthlyTotalMode => Mode == FocusGoalMode.MonthlyTotal;

    public FocusGoalRepeatMode RepeatMode
    {
        get => _repeatMode;
        private set
        {
            if (!SetField(ref _repeatMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEveryDay));
            OnPropertyChanged(nameof(IsCustomRepeat));
            OnPropertyChanged(nameof(DialogHeight));
        }
    }

    public bool IsEveryDay => RepeatMode == FocusGoalRepeatMode.EveryDay;

    public bool IsCustomRepeat => RepeatMode == FocusGoalRepeatMode.Custom;

    public int DailyTargetHours
    {
        get => _dailyTargetHours;
        private set => SetField(ref _dailyTargetHours, value);
    }

    public int MonthlyTargetHours
    {
        get => _monthlyTargetHours;
        private set => SetField(ref _monthlyTargetHours, value);
    }

    public int RemainingDays => 18;

    public int RemainingHours => 55;

    public double DialogHeight => IsDailyFixedMode && IsCustomRepeat ? 430 : 390;

    private void Open() => IsOpen = true;

    private void Close() => IsOpen = false;

    private static void ToggleWeekday(FocusGoalWeekdayOptionViewModel? weekday)
    {
        if (weekday is not null)
        {
            weekday.IsSelected = !weekday.IsSelected;
        }
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
