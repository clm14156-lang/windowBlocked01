using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public enum CompletedTaskTimeRange { All, Today, Week, Month }

public sealed class GoalCompletedTasksViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTime> _localNowProvider;
    private string? _goalId;
    private IReadOnlyList<LocalTaskDto> _tasks = [];
    private bool _isOpen, _isBusy;
    private CompletedTaskTimeRange _selectedRange;
    private bool _isSortDescending = true;
    private string _errorText = string.Empty;
    private string _searchQuery = string.Empty;
    private readonly RelayCommand<object> _open;
    private readonly RelayCommand<GoalCompletedTaskEntryViewModel> _restore, _delete;

    public GoalCompletedTasksViewModel(Func<DateTime>? localNowProvider = null)
    {
        _localNowProvider = localNowProvider ?? (() => DateTime.Now);
        _open = new(_ => Open(), _ => _goalId is not null);
        OpenCommand = _open;
        CloseCommand = new RelayCommand<object>(_ => IsOpen = false);
        ToggleSortCommand = new RelayCommand<object>(_ => IsSortDescending = !IsSortDescending);
        _restore = new(async entry => { if (entry is not null) await MutateAsync(entry, false); }, entry => entry is not null && !IsBusy);
        _delete = new(async entry => { if (entry is not null) await MutateAsync(entry, true); }, entry => entry is not null && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<GoalCompletedTaskEntryViewModel> Tasks { get; } = [];
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ToggleSortCommand { get; }
    public Func<LocalTaskDto, bool, Task<bool>>? MutationHandler { get; set; }
    public bool IsOpen { get => _isOpen; private set => Set(ref _isOpen, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { _restore.NotifyCanExecuteChanged(); _delete.NotifyCanExecuteChanged(); } }
    }
    public CompletedTaskTimeRange SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (!Enum.IsDefined(value) || !Set(ref _selectedRange, value)) return;
            NotifyRange();
            Refresh();
        }
    }
    public bool IsAllRange { get => SelectedRange == CompletedTaskTimeRange.All; set { if (value) SelectedRange = CompletedTaskTimeRange.All; } }
    public bool IsTodayRange { get => SelectedRange == CompletedTaskTimeRange.Today; set { if (value) SelectedRange = CompletedTaskTimeRange.Today; } }
    public bool IsWeekRange { get => SelectedRange == CompletedTaskTimeRange.Week; set { if (value) SelectedRange = CompletedTaskTimeRange.Week; } }
    public bool IsMonthRange { get => SelectedRange == CompletedTaskTimeRange.Month; set { if (value) SelectedRange = CompletedTaskTimeRange.Month; } }
    public string SearchQuery
    {
        get => _searchQuery;
        set { if (Set(ref _searchQuery, value ?? string.Empty)) Refresh(); }
    }
    public bool IsSortDescending
    {
        get => _isSortDescending;
        set
        {
            if (!Set(ref _isSortDescending, value)) return;
            Changed(nameof(IsSortAscending)); Changed(nameof(SortHint));
            Refresh();
        }
    }
    public bool IsSortAscending => !IsSortDescending;
    public string SortHint => IsSortDescending ? "当前按完成时间从新到旧，点击改为从旧到新" : "当前按完成时间从旧到新，点击改为从新到旧";
    public string ErrorText { get => _errorText; private set { if (Set(ref _errorText, value)) Changed(nameof(HasError)); } }
    public bool HasError => ErrorText.Length > 0;
    // The goal page retains its all-time total; the modal reports the filtered result count.
    public int TotalCount => _tasks.Count;
    public bool HasTasks => TotalCount > 0;
    public int FilteredCount => Tasks.Count;
    public string FilteredCountDisplay => $"共 {FilteredCount} 项";
    public bool HasResults => FilteredCount > 0;
    public string SummaryDisplay => $"已完成 {TotalCount} 项";
    public string EmptyText => !string.IsNullOrWhiteSpace(SearchQuery) ? "没有匹配的任务"
        : SelectedRange == CompletedTaskTimeRange.All ? "暂无已完成任务" : "该时间范围内暂无已完成任务";

    public void ApplyState(GoalOverviewItemViewModel? goal, IReadOnlyList<LocalTaskDto> tasks)
    {
        var changedGoal = _goalId != goal?.GoalId;
        _goalId = goal?.GoalId;
        _tasks = tasks.Where(task => _goalId is not null && task.TargetId == _goalId &&
                TaskCompletionPolicy.IsCompleted(task.IsCompleted, task.SubTasks.Select(child => child.IsCompleted)))
            .GroupBy(task => task.TaskId).Select(group => group.First()).ToArray();
        if (changedGoal)
        {
            IsOpen = false;
            ResetFilters();
            ErrorText = string.Empty;
        }
        Refresh();
        NotifyTotal();
        _open.NotifyCanExecuteChanged();
    }
    private void Open()
    {
        if (_goalId is null) return;
        ResetFilters(); ErrorText = string.Empty;
        Refresh(); IsOpen = true;
    }
    private void ResetFilters()
    {
        _selectedRange = CompletedTaskTimeRange.All;
        _isSortDescending = true;
        _searchQuery = string.Empty;
        Changed(nameof(SearchQuery));
        Changed(nameof(SelectedRange)); NotifyRange();
        Changed(nameof(IsSortDescending)); Changed(nameof(IsSortAscending)); Changed(nameof(SortHint));
    }
    private void NotifyRange()
    {
        Changed(nameof(IsAllRange)); Changed(nameof(IsTodayRange)); Changed(nameof(IsWeekRange)); Changed(nameof(IsMonthRange));
    }
    private void NotifyTotal()
    {
        Changed(nameof(TotalCount)); Changed(nameof(HasTasks)); Changed(nameof(SummaryDisplay));
    }
    private void Refresh()
    {
        var today = _localNowProvider().Date;
        var start = SelectedRange switch
        {
            CompletedTaskTimeRange.Today => today,
            CompletedTaskTimeRange.Week => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            CompletedTaskTimeRange.Month => new DateTime(today.Year, today.Month, 1),
            _ => (DateTime?)null
        };
        var end = SelectedRange switch
        {
            CompletedTaskTimeRange.Today => start?.AddDays(1),
            CompletedTaskTimeRange.Week => start?.AddDays(7),
            CompletedTaskTimeRange.Month => start?.AddMonths(1),
            _ => null
        };
        var filtered = _tasks.Where(task => start is null ||
            CompletionTime(task) is { } time && time >= start.Value && time < end!.Value);
        var query = SearchQuery.Trim();
        if (query.Length > 0)
            filtered = filtered.Where(task => task.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        // Missing legacy timestamps stay visible in All, at the end of either sort order.
        // UpdatedAtUtc is an edit time and must never be presented as a completion time.
        var knownFirst = filtered.OrderBy(task => task.CompletedAtUtc is null);
        var ordered = IsSortDescending ? knownFirst.ThenByDescending(task => task.CompletedAtUtc)
            : knownFirst.ThenBy(task => task.CompletedAtUtc);
        var rows = ordered.ThenBy(task => task.SortOrder).ThenBy(task => task.TaskId, StringComparer.Ordinal)
            .Select(task => new GoalCompletedTaskEntryViewModel(task, _restore, _delete)).ToArray();
        Tasks.Clear();
        foreach (var row in rows) Tasks.Add(row);
        Changed(nameof(FilteredCount)); Changed(nameof(FilteredCountDisplay)); Changed(nameof(HasResults)); Changed(nameof(EmptyText));
    }
    internal static DateTime? CompletionTime(LocalTaskDto task) => task.CompletedAtUtc?.LocalDateTime;

    public async Task<bool> MutateAsync(GoalCompletedTaskEntryViewModel entry, bool delete)
    {
        if (IsBusy || !_tasks.Any(task => task.TaskId == entry.Source.TaskId)) return false;
        IsBusy = true; ErrorText = string.Empty;
        try
        {
            if (MutationHandler is null || !await MutationHandler(entry.Source, delete))
            { ErrorText = delete ? "删除失败，请稍后重试" : "恢复失败，请稍后重试"; return false; }
            _tasks = _tasks.Where(task => task.TaskId != entry.Source.TaskId).ToArray();
            Refresh(); NotifyTotal();
            return true;
        }
        catch (Exception) { ErrorText = "操作未保存，请稍后重试"; return false; }
        finally { IsBusy = false; }
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record GoalCompletedTaskEntryViewModel(LocalTaskDto Source, ICommand RestoreCommand, ICommand DeleteCommand)
{
    public string Name => Source.Name;
    public string TimeDisplay => GoalCompletedTasksViewModel.CompletionTime(Source)?.ToString("M月d日 HH:mm") ?? "—";
}
