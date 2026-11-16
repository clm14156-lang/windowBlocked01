using System.ComponentModel;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalFocusCalendarViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTime> _now;
    private readonly RelayCommand<object> _open;
    private string? _goalId;
    private IReadOnlyList<GoalFocusDayViewModel> _days = [];
    private DateOnly _month;

    public GoalFocusCalendarViewModel(Func<DateTime> now)
    {
        _now = now;
        _open = new(_ => Open(), _ => _goalId is not null);
        CloseCommand = new RelayCommand<object>(_ => Close());
        PreviousMonthCommand = new RelayCommand<object>(_ => MoveMonth(-1));
        NextMonthCommand = new RelayCommand<object>(_ => MoveMonth(1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand OpenCommand => _open;
    public ICommand CloseCommand { get; }
    public ICommand PreviousMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public bool IsOpen { get; private set; }
    public string MonthDisplay => $"{_month:yyyy 年 M 月}";
    public string MonthTotalDisplay { get; private set; } = string.Empty;
    public bool HasMonthFocus => MonthTotalDisplay.Length > 0;
    public IReadOnlyList<GoalFocusCalendarDay> Cells { get; private set; } = [];

    internal void ApplyDays(string? goalId, IReadOnlyList<GoalFocusDayViewModel> days)
    {
        if (_goalId != goalId)
        {
            Close();
            _goalId = goalId;
            _open.NotifyCanExecuteChanged();
        }
        _days = days;
        if (IsOpen) RefreshCells();
    }

    private void Open()
    {
        if (_goalId is null) return;
        var today = _now();
        _month = new DateOnly(today.Year, today.Month, 1);
        RefreshCells();
        IsOpen = true;
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }

    internal void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }

    private void MoveMonth(int offset)
    {
        if (!IsOpen || offset < 0 && _month == DateOnly.MinValue ||
            offset > 0 && _month.Year == 9999 && _month.Month == 12) return;
        _month = _month.AddMonths(offset);
        RefreshCells();
    }

    private void RefreshCells()
    {
        var firstCell = _month.DayNumber - (((int)_month.DayOfWeek + 6) % 7);
        var durations = _days.Where(day => day.Date.Year == _month.Year && day.Date.Month == _month.Month)
            .ToDictionary(day => DateOnly.FromDateTime(day.Date), day => day.TotalDuration);
        var totalTicks = durations.Values.Sum(duration => duration.Ticks);
        MonthTotalDisplay = totalTicks switch
        {
            0 => string.Empty,
            < TimeSpan.TicksPerMinute => "累计 不足1分钟",
            _ => $"累计 {new GoalInvestmentDurationViewModel((int)(totalTicks / TimeSpan.TicksPerMinute)).Display}"
        };
        Cells = Enumerable.Range(firstCell, 42).Select(number =>
        {
            if (number < DateOnly.MinValue.DayNumber || number > DateOnly.MaxValue.DayNumber)
                return new GoalFocusCalendarDay(null, TimeSpan.Zero);
            var date = DateOnly.FromDayNumber(number);
            return new GoalFocusCalendarDay(date, durations.GetValueOrDefault(date));
        }).ToArray();
        PropertyChanged?.Invoke(this, new(nameof(MonthDisplay)));
        PropertyChanged?.Invoke(this, new(nameof(MonthTotalDisplay)));
        PropertyChanged?.Invoke(this, new(nameof(HasMonthFocus)));
        PropertyChanged?.Invoke(this, new(nameof(Cells)));
    }
}

public sealed record GoalFocusCalendarDay(DateOnly? Date, TimeSpan Duration)
{
    public bool HasFocus => Duration > TimeSpan.Zero;
    public string DayDisplay => Date?.Day.ToString() ?? string.Empty;
    public string DurationDisplay
    {
        get
        {
            if (!HasFocus) return string.Empty;
            var minutes = (int)Duration.TotalMinutes;
            if (minutes == 0) return "<1m";
            if (minutes < 60) return $"{minutes}m";
            var hours = minutes / 60;
            return minutes % 60 == 0 ? $"{hours}h" : $"{hours}h{minutes % 60}m";
        }
    }
}
