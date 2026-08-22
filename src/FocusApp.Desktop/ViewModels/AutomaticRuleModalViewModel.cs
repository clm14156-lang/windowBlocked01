using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AutomaticRuleModalViewModel : INotifyPropertyChanged
{
    private const int MinutesPerDay = 24 * 60;
    private const int TimeStepMinutes = 5;
    private bool _isOpen;
    private bool _isCustom;
    private bool _isTimePickerOpen;
    private bool _isPickingStartTime;
    private bool _hasStartTime = true;
    private bool _hasEndTime = true;
    private double _startValue = 9 * 60;
    private double _endValue = 12 * 60;
    private string _startTimeText = "09:00";
    private string _endTimeText = "12:00";
    private TimePickerOptionViewModel? _selectedHour;
    private TimePickerOptionViewModel? _selectedMinute;
    private readonly ObservableCollection<TimeWheelItemViewModel> _hourWheelItems = [];
    private readonly ObservableCollection<TimeWheelItemViewModel> _minuteWheelItems = [];
    private string _validationMessage = string.Empty;

    public AutomaticRuleModalViewModel(IEnumerable<WeekdayOptionViewModel> weekdays)
    {
        Weekdays = new ReadOnlyCollection<WeekdayOptionViewModel>(weekdays.ToList());
        HourOptions = new ReadOnlyCollection<TimePickerOptionViewModel>(
            Enumerable.Range(0, 24).Select(value => new TimePickerOptionViewModel(value, $"{value:00}")).ToList());
        MinuteOptions = new ReadOnlyCollection<TimePickerOptionViewModel>(
            Enumerable.Range(0, 12)
                .Select(value => value * TimeStepMinutes)
                .Select(value => new TimePickerOptionViewModel(value, $"{value:00}"))
                .ToList());
        HourWheelItems = new ReadOnlyObservableCollection<TimeWheelItemViewModel>(_hourWheelItems);
        MinuteWheelItems = new ReadOnlyObservableCollection<TimeWheelItemViewModel>(_minuteWheelItems);
        foreach (var weekday in Weekdays)
        {
            weekday.PropertyChanged += Weekday_PropertyChanged;
        }

        SelectDailyCommand = new RelayCommand<object>(_ => IsCustom = false);
        SelectCustomCommand = new RelayCommand<object>(_ => IsCustom = true);
        CloseCommand = new RelayCommand<object>(_ => Close());
        ConfirmCommand = new RelayCommand<object>(_ => Confirm());
        ClearTimePickerCommand = new RelayCommand<object>(_ => ClearTimePicker());
        ConfirmTimePickerCommand = new RelayCommand<object>(_ => ConfirmTimePicker());
        SelectHourWheelItemCommand = new RelayCommand<TimeWheelItemViewModel>(SelectHourWheelItem);
        SelectMinuteWheelItemCommand = new RelayCommand<TimeWheelItemViewModel>(SelectMinuteWheelItem);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<AutomaticRuleDraft>? RuleCreated;

    public Func<AutomaticRuleDraft, string?>? ValidateRule { get; set; }

    public ReadOnlyCollection<WeekdayOptionViewModel> Weekdays { get; }

    public ReadOnlyCollection<TimePickerOptionViewModel> HourOptions { get; }

    public ReadOnlyCollection<TimePickerOptionViewModel> MinuteOptions { get; }

    public ReadOnlyObservableCollection<TimeWheelItemViewModel> HourWheelItems { get; }

    public ReadOnlyObservableCollection<TimeWheelItemViewModel> MinuteWheelItems { get; }

    public ICommand SelectDailyCommand { get; }

    public ICommand SelectCustomCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand ClearTimePickerCommand { get; }

    public ICommand ConfirmTimePickerCommand { get; }

    public ICommand SelectHourWheelItemCommand { get; }

    public ICommand SelectMinuteWheelItemCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool IsCustom
    {
        get => _isCustom;
        set
        {
            if (SetField(ref _isCustom, value))
            {
                OnPropertyChanged(nameof(IsDaily));
            }
        }
    }

    public bool IsDaily => !IsCustom;

    public bool IsTimePickerOpen
    {
        get => _isTimePickerOpen;
        set
        {
            if (SetField(ref _isTimePickerOpen, value))
            {
                OnPropertyChanged(nameof(IsStartTimePickerOpen));
                OnPropertyChanged(nameof(IsEndTimePickerOpen));
            }
        }
    }

    public bool IsStartTimePickerOpen => IsTimePickerOpen && _isPickingStartTime;

    public bool IsEndTimePickerOpen => IsTimePickerOpen && !_isPickingStartTime;

    public TimePickerOptionViewModel? SelectedHour
    {
        get => _selectedHour;
        set
        {
            if (SetField(ref _selectedHour, value))
            {
                RefreshHourWheelItems();
            }
        }
    }

    public TimePickerOptionViewModel? SelectedMinute
    {
        get => _selectedMinute;
        set
        {
            if (SetField(ref _selectedMinute, value))
            {
                RefreshMinuteWheelItems();
            }
        }
    }

    public double StartValue
    {
        get => _startValue;
        set
        {
            var coerced = Math.Clamp(SnapToStep(value), 0, EndValue);
            if (SetField(ref _startValue, coerced))
            {
                _hasStartTime = true;
                SetStartText(FormatTime(coerced));
                if (IsTimePickerOpen && _isPickingStartTime)
                {
                    SyncPickerSelection(coerced);
                }
            }
        }
    }

    public double EndValue
    {
        get => _endValue;
        set
        {
            var coerced = Math.Clamp(SnapToStep(value), StartValue, MinutesPerDay);
            if (SetField(ref _endValue, coerced))
            {
                _hasEndTime = true;
                SetEndText(FormatTime(coerced));
                if (IsTimePickerOpen && !_isPickingStartTime)
                {
                    SyncPickerSelection(coerced);
                }
            }
        }
    }

    public string StartTimeText
    {
        get => _startTimeText;
        set
        {
            if (!SetField(ref _startTimeText, value))
            {
                return;
            }

            if (TryParseTime(value, out var minutes))
            {
                _hasStartTime = true;
                StartValue = Math.Min(minutes, EndValue);
                SetStartText(FormatTime(StartValue));
            }
        }
    }

    public string EndTimeText
    {
        get => _endTimeText;
        set
        {
            if (!SetField(ref _endTimeText, value))
            {
                return;
            }

            if (TryParseTime(value, out var minutes))
            {
                _hasEndTime = true;
                EndValue = Math.Max(minutes, StartValue);
                SetEndText(FormatTime(EndValue));
            }
        }
    }

    public string SelectedDaysText => string.Join("、", Weekdays.Where(day => day.IsSelected).Select(day => day.DisplayName));

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetField(ref _validationMessage, value);
    }

    public void Open()
    {
        IsCustom = false;
        IsTimePickerOpen = false;
        ValidationMessage = string.Empty;
        _hasStartTime = true;
        _hasEndTime = true;
        StartValue = 9 * 60;
        EndValue = 12 * 60;
        SetStartText("09:00");
        SetEndText("12:00");
        for (var index = 0; index < Weekdays.Count; index++)
        {
            Weekdays[index].IsSelected = index is 0 or 2 or 4;
        }

        IsOpen = true;
    }

    public void OpenTimePicker(bool isStartTime)
    {
        _isPickingStartTime = isStartTime;
        var value = isStartTime
            ? (_hasStartTime ? StartValue : 0)
            : (_hasEndTime ? EndValue : MinutesPerDay);
        SyncPickerSelection(value);
        IsTimePickerOpen = true;
        OnPropertyChanged(nameof(IsStartTimePickerOpen));
        OnPropertyChanged(nameof(IsEndTimePickerOpen));
    }

    public void CloseTimePicker()
    {
        IsTimePickerOpen = false;
    }

    public void AdjustTimeByWheel(bool isStartTime, int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var delta = direction > 0 ? TimeStepMinutes : -TimeStepMinutes;
        if (isStartTime)
        {
            var current = _hasStartTime ? SnapToStep(StartValue) : 0;
            var adjusted = Math.Clamp(current + delta, 0, EndValue);
            _hasStartTime = true;
            StartValue = adjusted;
            SetStartText(FormatTime(StartValue));
        }
        else
        {
            var current = _hasEndTime ? SnapToStep(EndValue) : MinutesPerDay;
            var adjusted = Math.Clamp(current + delta, StartValue, MinutesPerDay);
            _hasEndTime = true;
            EndValue = adjusted;
            SetEndText(FormatTime(EndValue));
        }

        ValidationMessage = string.Empty;
    }

    public void AdjustPickerWheel(bool isHourColumn, int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var offset = direction > 0 ? -1 : 1;
        if (isHourColumn)
        {
            var current = SelectedHour?.Value ?? 0;
            var next = (current + offset + HourOptions.Count) % HourOptions.Count;
            SelectedHour = HourOptions[next];
            return;
        }

        var minuteIndex = SelectedMinute is null ? 0 : MinuteOptions.IndexOf(SelectedMinute);
        var nextMinuteIndex = (minuteIndex + offset + MinuteOptions.Count) % MinuteOptions.Count;
        SelectedMinute = MinuteOptions[nextMinuteIndex];
    }

    public static AutomaticRuleModalViewModel CreateDefault()
    {
        return new AutomaticRuleModalViewModel(
        [
            new("Monday", "Mon", "1", true),
            new("Tuesday", "Tue", "2", false),
            new("Wednesday", "Wed", "3", true),
            new("Thursday", "Thu", "4", false),
            new("Friday", "Fri", "5", true),
            new("Saturday", "Sat", "6", false),
            new("Sunday", "Sun", "7", false)
        ]);
    }

    private void Confirm()
    {
        if (!_hasStartTime || !_hasEndTime)
        {
            ValidationMessage = "请选择开始时间和结束时间";
            return;
        }

        var selectedDays = (IsCustom ? Weekdays.Where(day => day.IsSelected) : Weekdays).ToArray();
        var draft = new AutomaticRuleDraft(
            IsCustom,
            selectedDays,
            FormatTime(StartValue),
            FormatTime(EndValue),
            StartValue,
            EndValue);
        var validationMessage = ValidateRule?.Invoke(draft);
        if (!string.IsNullOrEmpty(validationMessage))
        {
            ValidationMessage = validationMessage;
            return;
        }

        RuleCreated?.Invoke(this, draft);
        Close();
    }

    private void Close()
    {
        IsTimePickerOpen = false;
        IsOpen = false;
    }

    private void ClearTimePicker()
    {
        if (_isPickingStartTime)
        {
            _hasStartTime = false;
            SetField(ref _startValue, 0, nameof(StartValue));
            SetStartText("--:--");
        }
        else
        {
            _hasEndTime = false;
            SetField(ref _endValue, MinutesPerDay, nameof(EndValue));
            SetEndText("--:--");
        }

        ValidationMessage = string.Empty;
        IsTimePickerOpen = false;
    }

    private void ConfirmTimePicker()
    {
        if (SelectedHour is null || SelectedMinute is null)
        {
            return;
        }

        var selectedMinutes = SelectedHour.Value * 60 + SelectedMinute.Value;
        if (_isPickingStartTime)
        {
            _hasStartTime = true;
            StartValue = Math.Min(selectedMinutes, EndValue);
            SetStartText(FormatTime(StartValue));
        }
        else
        {
            _hasEndTime = true;
            EndValue = Math.Max(selectedMinutes, StartValue);
            SetEndText(FormatTime(EndValue));
        }

        ValidationMessage = string.Empty;
        IsTimePickerOpen = false;
    }

    private void Weekday_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WeekdayOptionViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedDaysText));
        }
    }

    private void SetStartText(string value)
    {
        if (_startTimeText != value)
        {
            _startTimeText = value;
            OnPropertyChanged(nameof(StartTimeText));
        }
    }

    private void SetEndText(string value)
    {
        if (_endTimeText != value)
        {
            _endTimeText = value;
            OnPropertyChanged(nameof(EndTimeText));
        }
    }

    private static bool TryParseTime(string value, out double minutes)
    {
        minutes = 0;
        var parts = value.Split(':');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 2 || parts[1].Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutePart) ||
            hours is < 0 or > 24 || minutePart is < 0 or > 59 || (hours == 24 && minutePart != 0))
        {
            return false;
        }

        minutes = hours * 60 + minutePart;
        return true;
    }

    private static string FormatTime(double minutes)
    {
        var totalMinutes = (int)Math.Round(minutes);
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    private static double SnapToStep(double minutes)
    {
        return Math.Clamp(Math.Round(minutes / TimeStepMinutes) * TimeStepMinutes, 0, MinutesPerDay);
    }

    private void SyncPickerSelection(double minutes)
    {
        var snappedValue = Math.Min(SnapToStep(minutes), MinutesPerDay - TimeStepMinutes);
        var hour = (int)snappedValue / 60;
        var minute = (int)snappedValue % 60;
        SelectedHour = HourOptions[hour];
        SelectedMinute = MinuteOptions.Single(option => option.Value == minute);
    }

    private void SelectHourWheelItem(TimeWheelItemViewModel? item)
    {
        if (item is not null)
        {
            SelectedHour = HourOptions[item.Value];
        }
    }

    private void SelectMinuteWheelItem(TimeWheelItemViewModel? item)
    {
        if (item is not null)
        {
            SelectedMinute = MinuteOptions.Single(option => option.Value == item.Value);
        }
    }

    private void RefreshHourWheelItems()
    {
        if (SelectedHour is null)
        {
            return;
        }

        _hourWheelItems.Clear();
        for (var offset = -2; offset <= 2; offset++)
        {
            var value = (SelectedHour.Value + offset + HourOptions.Count) % HourOptions.Count;
            _hourWheelItems.Add(new TimeWheelItemViewModel(value, $"{value:00}", offset));
        }
    }

    private void RefreshMinuteWheelItems()
    {
        if (SelectedMinute is null)
        {
            return;
        }

        var selectedIndex = MinuteOptions.IndexOf(SelectedMinute);
        _minuteWheelItems.Clear();
        for (var offset = -2; offset <= 2; offset++)
        {
            var optionIndex = (selectedIndex + offset + MinuteOptions.Count) % MinuteOptions.Count;
            var option = MinuteOptions[optionIndex];
            _minuteWheelItems.Add(new TimeWheelItemViewModel(option.Value, option.Display, offset));
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record TimePickerOptionViewModel(int Value, string Display);

public sealed record TimeWheelItemViewModel(int Value, string Display, int Offset)
{
    public bool IsSelected => Offset == 0;

    public double Opacity => Math.Abs(Offset) switch
    {
        0 => 1,
        1 => 0.62,
        _ => 0.32
    };
}

public sealed class WeekdayOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public WeekdayOptionViewModel(string key, string displayName, string shortName, bool isSelected)
    {
        Key = key;
        DisplayName = displayName;
        ShortName = shortName;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string DisplayName { get; }

    public string ShortName { get; }

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

public sealed record AutomaticRuleDraft(
    bool IsCustom,
    IReadOnlyList<WeekdayOptionViewModel> SelectedDays,
    string StartTime,
    string EndTime,
    double StartMinutes,
    double EndMinutes);
