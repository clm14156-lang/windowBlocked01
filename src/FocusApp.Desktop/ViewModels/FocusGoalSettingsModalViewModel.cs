using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
    private const int DailyTargetMaximumHours = 24;
    private const int MonthlyTargetMaximumHours = 720;

    private bool _isOpen;
    private FocusGoalMode _mode = FocusGoalMode.DailyFixed;
    private FocusGoalRepeatMode _repeatMode = FocusGoalRepeatMode.EveryDay;
    private int _dailyTargetHours = 4;
    private string _dailyTargetHoursInput = "4";
    private int _monthlyTargetHours = 60;
    private string _monthlyTargetHoursInput = "60";
    private bool _isMoreMenuOpen;
    private bool _hasSavedTarget;
    private bool _hasSavedDailyFixedTarget;

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
        SaveCommand = new RelayCommand<object>(_ =>
        {
            CommitTargetHoursInput(IsMonthlyTotalMode);
            _hasSavedTarget = true;
            OnPropertyChanged(nameof(HasSavedTarget));
            _hasSavedDailyFixedTarget = IsDailyFixedMode;
            OnPropertyChanged(nameof(HasSavedDailyFixedTarget));
            DailyFixedTargetChanged?.Invoke(this, EventArgs.Empty);
            Close();
        });
        ToggleMoreMenuCommand = new RelayCommand<object>(_ => ToggleMoreMenu());
        DeleteTargetCommand = new RelayCommand<object>(_ => DeleteTarget());
        SelectDailyModeCommand = new RelayCommand<object>(_ => Mode = FocusGoalMode.DailyFixed);
        SelectMonthlyModeCommand = new RelayCommand<object>(_ => Mode = FocusGoalMode.MonthlyTotal);
        SelectEveryDayCommand = new RelayCommand<object>(_ => RepeatMode = FocusGoalRepeatMode.EveryDay);
        SelectCustomRepeatCommand = new RelayCommand<object>(_ => RepeatMode = FocusGoalRepeatMode.Custom);
        ToggleWeekdayCommand = new RelayCommand<FocusGoalWeekdayOptionViewModel>(ToggleWeekday);
        IncreaseDailyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(false, 1));
        DecreaseDailyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(false, -1));
        IncreaseMonthlyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(true, 1));
        DecreaseMonthlyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(true, -1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DailyFixedTargetChanged;

    public ObservableCollection<FocusGoalWeekdayOptionViewModel> Weekdays { get; }

    public ICommand OpenCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ToggleMoreMenuCommand { get; }

    public ICommand DeleteTargetCommand { get; }

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

    public bool IsMoreMenuOpen
    {
        get => _isMoreMenuOpen;
        set => SetField(ref _isMoreMenuOpen, value);
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

    public bool HasSavedDailyFixedTarget => _hasSavedDailyFixedTarget;

    public bool HasSavedTarget => _hasSavedTarget;

    public int DailyTargetHours
    {
        get => _dailyTargetHours;
        private set
        {
            if (!SetField(ref _dailyTargetHours, value))
            {
                return;
            }

            DailyTargetHoursInput = FormatTargetHours(value);
        }
    }

    public string DailyTargetHoursInput
    {
        get => _dailyTargetHoursInput;
        set
        {
            var normalizedValue = NormalizeTargetHoursInput(value, DailyTargetMaximumHours);
            if (!SetField(ref _dailyTargetHoursInput, normalizedValue))
            {
                return;
            }

            if (TryParseTargetHours(normalizedValue, DailyTargetMaximumHours, out var parsedValue))
            {
                SetField(ref _dailyTargetHours, parsedValue);
            }
        }
    }

    public int MonthlyTargetHours
    {
        get => _monthlyTargetHours;
        private set
        {
            if (!SetField(ref _monthlyTargetHours, value))
            {
                return;
            }

            MonthlyTargetHoursInput = FormatTargetHours(value);
        }
    }

    public string MonthlyTargetHoursInput
    {
        get => _monthlyTargetHoursInput;
        set
        {
            var normalizedValue = NormalizeTargetHoursInput(value, MonthlyTargetMaximumHours);
            if (!SetField(ref _monthlyTargetHoursInput, normalizedValue))
            {
                return;
            }

            if (TryParseTargetHours(normalizedValue, MonthlyTargetMaximumHours, out var parsedValue))
            {
                SetField(ref _monthlyTargetHours, parsedValue);
            }
        }
    }

    public int RemainingDays => 18;

    public int RemainingHours => 55;

    public double DialogHeight => IsDailyFixedMode && IsCustomRepeat ? 430 : 390;

    public void CommitDailyTargetHoursInput()
    {
        CommitTargetHoursInput(false);
    }

    public void CommitTargetHoursInput(bool isMonthly)
    {
        var input = isMonthly ? MonthlyTargetHoursInput : DailyTargetHoursInput;
        var currentValue = isMonthly ? MonthlyTargetHours : DailyTargetHours;
        var maximumValue = isMonthly ? MonthlyTargetMaximumHours : DailyTargetMaximumHours;
        if (!TryParseTargetHours(input, maximumValue, out var value))
        {
            if (isMonthly)
            {
                MonthlyTargetHoursInput = FormatTargetHours(currentValue);
            }
            else
            {
                DailyTargetHoursInput = FormatTargetHours(currentValue);
            }

            return;
        }

        if (isMonthly)
        {
            MonthlyTargetHours = value;
        }
        else
        {
            DailyTargetHours = value;
        }
    }

    private void Open()
    {
        IsMoreMenuOpen = false;
        IsOpen = true;
    }

    private void Close()
    {
        IsMoreMenuOpen = false;
        IsOpen = false;
    }

    private void ToggleMoreMenu()
    {
        if (!HasSavedTarget)
        {
            IsMoreMenuOpen = false;
            return;
        }

        IsMoreMenuOpen = !IsMoreMenuOpen;
    }

    private void DeleteTarget()
    {
        if (!HasSavedTarget)
        {
            IsMoreMenuOpen = false;
            return;
        }

        _hasSavedTarget = false;
        OnPropertyChanged(nameof(HasSavedTarget));
        _hasSavedDailyFixedTarget = false;
        OnPropertyChanged(nameof(HasSavedDailyFixedTarget));
        Mode = FocusGoalMode.DailyFixed;
        RepeatMode = FocusGoalRepeatMode.EveryDay;
        DailyTargetHours = 4;
        MonthlyTargetHours = 60;
        for (var index = 0; index < Weekdays.Count; index++)
        {
            Weekdays[index].IsSelected = index < 5;
        }

        DailyFixedTargetChanged?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void AdjustTargetHours(bool isMonthly, int delta)
    {
        CommitTargetHoursInput(isMonthly);
        if (isMonthly)
        {
            MonthlyTargetHours = Math.Clamp(MonthlyTargetHours + delta, 0, MonthlyTargetMaximumHours);
        }
        else
        {
            DailyTargetHours = Math.Clamp(DailyTargetHours + delta, 0, DailyTargetMaximumHours);
        }
    }

    private static bool TryParseTargetHours(string input, int maximumValue, out int value)
    {
        if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        value = Math.Clamp(value, 0, maximumValue);
        return true;
    }

    private static string NormalizeTargetHoursInput(string input, int maximumValue)
    {
        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > maximumValue)
        {
            return FormatTargetHours(maximumValue);
        }

        return input;
    }

    private static string FormatTargetHours(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

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
