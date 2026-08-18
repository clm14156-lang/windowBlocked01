using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AutomaticRuleModalViewModel : INotifyPropertyChanged
{
    private const int MinutesPerDay = 24 * 60;
    private bool _isOpen;
    private bool _isCustom;
    private double _startValue = 9 * 60;
    private double _endValue = 12 * 60;
    private string _startTimeText = "09:00";
    private string _endTimeText = "12:00";
    private string _validationMessage = string.Empty;

    public AutomaticRuleModalViewModel(IEnumerable<WeekdayOptionViewModel> weekdays)
    {
        Weekdays = new ReadOnlyCollection<WeekdayOptionViewModel>(weekdays.ToList());
        foreach (var weekday in Weekdays)
        {
            weekday.PropertyChanged += Weekday_PropertyChanged;
        }

        SelectDailyCommand = new RelayCommand<object>(_ => IsCustom = false);
        SelectCustomCommand = new RelayCommand<object>(_ => IsCustom = true);
        CloseCommand = new RelayCommand<object>(_ => Close());
        ConfirmCommand = new RelayCommand<object>(_ => Confirm());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<AutomaticRuleDraft>? RuleCreated;

    public Func<AutomaticRuleDraft, string?>? ValidateRule { get; set; }

    public ReadOnlyCollection<WeekdayOptionViewModel> Weekdays { get; }

    public ICommand SelectDailyCommand { get; }

    public ICommand SelectCustomCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand ConfirmCommand { get; }

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

    public double StartValue
    {
        get => _startValue;
        set
        {
            var coerced = Math.Clamp(Math.Round(value), 0, EndValue);
            if (SetField(ref _startValue, coerced))
            {
                SetStartText(FormatTime(coerced));
            }
        }
    }

    public double EndValue
    {
        get => _endValue;
        set
        {
            var coerced = Math.Clamp(Math.Round(value), StartValue, MinutesPerDay);
            if (SetField(ref _endValue, coerced))
            {
                SetEndText(FormatTime(coerced));
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
        ValidationMessage = string.Empty;
        StartValue = 9 * 60;
        EndValue = 12 * 60;
        for (var index = 0; index < Weekdays.Count; index++)
        {
            Weekdays[index].IsSelected = index is 0 or 2 or 4;
        }

        IsOpen = true;
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
        IsOpen = false;
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
