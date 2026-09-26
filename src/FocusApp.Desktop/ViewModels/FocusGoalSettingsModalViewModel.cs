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

public sealed class FocusGoalSettingsModalViewModel : INotifyPropertyChanged
{
    private const int DailyTargetMaximumHours = 24;
    private const int MonthlyTargetMaximumHours = 720;

    private bool _isOpen;
    private FocusGoalMode _mode = FocusGoalMode.DailyFixed;
    private int _dailyTargetHours = 4;
    private string _dailyTargetHoursInput = "4";
    private int _monthlyTargetHours = 60;
    private string _monthlyTargetHoursInput = "60";
    private bool _isMoreMenuOpen;
    private bool _hasSavedTarget;
    private bool _hasSavedDailyFixedTarget;
    private readonly Func<DateTime> _localNowProvider;
    private readonly Func<TimeSpan> _monthlyCompletedFocusProvider;

    public FocusGoalSettingsModalViewModel(
        Func<DateTime>? localNowProvider = null,
        Func<TimeSpan>? monthlyCompletedFocusProvider = null)
    {
        _localNowProvider = localNowProvider ?? (() => DateTime.Now);
        _monthlyCompletedFocusProvider = monthlyCompletedFocusProvider ?? (() => TimeSpan.Zero);
        OpenCommand = new RelayCommand<object>(_ => Open());
        CancelCommand = new RelayCommand<object>(_ => Close());
        SaveCommand = new RelayCommand<object>(_ =>
        {
            CommitTargetHoursInput(IsMonthlyTotalMode);
            _hasSavedTarget = true;
            OnPropertyChanged(nameof(HasSavedTarget));
            _hasSavedDailyFixedTarget = IsDailyFixedMode;
            OnPropertyChanged(nameof(HasSavedDailyFixedTarget));
            GoalSettingsChanged?.Invoke(this, EventArgs.Empty);
            DailyFixedTargetChanged?.Invoke(this, EventArgs.Empty);
            Close();
        });
        ToggleMoreMenuCommand = new RelayCommand<object>(_ => ToggleMoreMenu());
        DeleteTargetCommand = new RelayCommand<object>(_ => DeleteTarget());
        SelectDailyModeCommand = new RelayCommand<object>(_ => Mode = FocusGoalMode.DailyFixed);
        SelectMonthlyModeCommand = new RelayCommand<object>(_ => Mode = FocusGoalMode.MonthlyTotal);
        IncreaseDailyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(false, 1));
        DecreaseDailyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(false, -1));
        IncreaseMonthlyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(true, 1));
        DecreaseMonthlyTargetCommand = new RelayCommand<object>(_ => AdjustTargetHours(true, -1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? DailyFixedTargetChanged;

    public event EventHandler? GoalSettingsChanged;

    public ICommand OpenCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand ToggleMoreMenuCommand { get; }

    public ICommand DeleteTargetCommand { get; }

    public ICommand SelectDailyModeCommand { get; }

    public ICommand SelectMonthlyModeCommand { get; }

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
            RefreshMonthlyProgress();
        }
    }

    public bool IsDailyFixedMode => Mode == FocusGoalMode.DailyFixed;

    public bool IsMonthlyTotalMode => Mode == FocusGoalMode.MonthlyTotal;

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
            RefreshMonthlyProgress();
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
                if (SetField(ref _monthlyTargetHours, parsedValue)) RefreshMonthlyProgress();
            }
        }
    }

    public int RemainingDays
    {
        get
        {
            var today = _localNowProvider().Date;
            return DateTime.DaysInMonth(today.Year, today.Month) - today.Day + 1;
        }
    }

    public double RemainingHours => Math.Max(0, MonthlyTargetHours - Math.Max(0, _monthlyCompletedFocusProvider().TotalHours));

    public double DailyRequiredFocusHours => RemainingHours / RemainingDays;

    public string MonthlyDailyRequirementDisplay => FormattableString.Invariant(
        $"按当前进度，每天约需 {DailyRequiredFocusHours:0.0} 小时");

    public double DialogHeight => IsMonthlyTotalMode ? 410 : 350;

    public void RefreshMonthlyProgress()
    {
        OnPropertyChanged(nameof(RemainingDays));
        OnPropertyChanged(nameof(RemainingHours));
        OnPropertyChanged(nameof(DailyRequiredFocusHours));
        OnPropertyChanged(nameof(MonthlyDailyRequirementDisplay));
    }

    public void CommitDailyTargetHoursInput()
    {
        CommitTargetHoursInput(false);
    }

    public void ApplyPersistedMonthlyTarget(int? targetHours)
    {
        if (targetHours is > 0)
        {
            MonthlyTargetHours = Math.Clamp(targetHours.Value, 0, MonthlyTargetMaximumHours);
            Mode = FocusGoalMode.MonthlyTotal;
            _hasSavedTarget = true;
            OnPropertyChanged(nameof(HasSavedTarget));
            _hasSavedDailyFixedTarget = false;
            OnPropertyChanged(nameof(HasSavedDailyFixedTarget));
            IsMoreMenuOpen = false;
            return;
        }

        if (!HasSavedTarget || !IsMonthlyTotalMode)
        {
            return;
        }

        _hasSavedTarget = false;
        OnPropertyChanged(nameof(HasSavedTarget));
        _hasSavedDailyFixedTarget = false;
        OnPropertyChanged(nameof(HasSavedDailyFixedTarget));
        Mode = FocusGoalMode.DailyFixed;
        IsMoreMenuOpen = false;
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
        RefreshMonthlyProgress();
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
        DailyTargetHours = 4;
        MonthlyTargetHours = 60;
        DailyFixedTargetChanged?.Invoke(this, EventArgs.Empty);
        GoalSettingsChanged?.Invoke(this, EventArgs.Empty);
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
