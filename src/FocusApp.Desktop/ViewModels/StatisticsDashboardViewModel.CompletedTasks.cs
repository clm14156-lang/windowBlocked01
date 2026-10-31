using System.Globalization;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed partial class StatisticsDashboardViewModel
{
    public const int CompletedTasksPageSize = 10;
    private IReadOnlyList<OverviewCompletedTaskEntry> _periodCompletedTasks = [];
    private OverviewCompletedTaskEntry[] _filteredCompletedTasks = [];
    private bool _isCompletedTasksOpen;
    private string? _completedGoalId;
    private bool _completedNewestFirst = true;
    private int _completedTasksPage = 1;
    private readonly RelayCommand<object> _previousCompletedTasksPage, _nextCompletedTasksPage;
    private readonly RelayCommand<CompletedTaskPageOption> _goToCompletedTasksPage;

    public ICommand OpenCompletedTasksCommand { get; }
    public ICommand CloseCompletedTasksCommand { get; }
    public ICommand PreviousCompletedTasksPageCommand => _previousCompletedTasksPage;
    public ICommand NextCompletedTasksPageCommand => _nextCompletedTasksPage;
    public ICommand GoToCompletedTasksPageCommand => _goToCompletedTasksPage;
    public bool IsCompletedTasksOpen
    {
        get => _isCompletedTasksOpen;
        private set { if (_isCompletedTasksOpen == value) return; _isCompletedTasksOpen = value; OnPropertyChanged(); }
    }
    public IReadOnlyList<CompletedGoalOption> CompletedGoalOptions { get; private set; } = [new(null, "全部目标")];
    public CompletedGoalOption SelectedCompletedGoal
    {
        get => CompletedGoalOptions.First(option => option.GoalId == _completedGoalId);
        set
        {
            if (value is null || value.GoalId == _completedGoalId || !CompletedGoalOptions.Contains(value)) return;
            _completedGoalId = value.GoalId;
            RefreshCompletedTasks(resetPage: true);
            NotifyCompletedTasks();
        }
    }
    public IReadOnlyList<CompletedSortOption> CompletedSortOptions { get; } = [new(true, "最新完成"), new(false, "最早完成")];
    public CompletedSortOption SelectedCompletedSort
    {
        get => CompletedSortOptions[_completedNewestFirst ? 0 : 1];
        set
        {
            if (value is null || value.NewestFirst == _completedNewestFirst || !CompletedSortOptions.Contains(value)) return;
            _completedNewestFirst = value.NewestFirst;
            RefreshCompletedTasks(resetPage: true);
            NotifyCompletedTasks();
        }
    }
    public int CompletedTasksPage => _completedTasksPage;
    public int CompletedTasksPageCount => Math.Max(1, (_filteredCompletedTasks.Length + CompletedTasksPageSize - 1) / CompletedTasksPageSize);
    public int FilteredCompletedTaskCount => _filteredCompletedTasks.Length;
    public bool HasCompletedTaskResults => FilteredCompletedTaskCount > 0;
    public string CompletedTasksListTitle => $"任务列表（共 {FilteredCompletedTaskCount.ToString("N0", CultureInfo.InvariantCulture)} 项）";
    public string CompletedTasksTotalDisplay => $"共 {FilteredCompletedTaskCount.ToString("N0", CultureInfo.InvariantCulture)} 项";
    public IReadOnlyList<OverviewCompletedTaskEntry> CompletedTasksPageItems { get; private set; } = [];
    public IReadOnlyList<CompletedTaskPageOption> CompletedTasksPageOptions { get; private set; } = [];

    private void OpenCompletedTasks()
    {
        RefreshCurrentDate();
        IsPeriodPickerOpen = false;
        IsFocusDaysDetailsOpen = false;
        _completedGoalId = null;
        _completedNewestFirst = true;
        RefreshCompletedTasks(resetPage: true);
        NotifyCompletedTasks();
        IsCompletedTasksOpen = true;
    }

    private IReadOnlyList<OverviewCompletedTaskEntry> BuildPeriodCompletedTasks()
    {
        var entries = new Dictionary<string, OverviewCompletedTaskEntry>(StringComparer.Ordinal);
        // Reuse the overview's existing valid-history membership and ID deduplication.
        // A live task's current status/updated time must not rewrite saved history.
        for (var recordIndex = 0; recordIndex < _records.Length; recordIndex++)
        {
            var record = _records[recordIndex];
            if (record.FocusDuration < TimeSpan.FromMinutes(5)) continue;
            var goalId = NormalizeGoalId(record.GoalId);
            var goal = _goals.FirstOrDefault(item => item.GoalId == goalId);
            var goalName = goalId == "goal-unassigned" ? "未关联目标"
                : !string.IsNullOrWhiteSpace(goal?.Name) ? goal.Name
                : !string.IsNullOrWhiteSpace(record.GoalName) ? record.GoalName : "未命名目标";

            void Add(string? taskId, string? name, DateTime? completedAt, int index)
            {
                // Keep the existing period assignment for legacy records without a saved
                // completion timestamp, but display an unknown date rather than inventing one.
                var membershipDate = completedAt ?? record.EndTime;
                if (membershipDate < PeriodStart || membershipDate >= PeriodEndExclusive) return;
                var key = string.IsNullOrWhiteSpace(taskId) ? $"legacy:{recordIndex}:{index}" : $"task:{taskId}";
                var row = new OverviewCompletedTaskEntry(key, taskId, string.IsNullOrWhiteSpace(name) ? "未命名任务" : name,
                    goalId, goalName, goal?.IconSource ?? record.IconSource, completedAt);
                if (!entries.TryGetValue(key, out var previous) ||
                    row.CompletedAt is { } time && (previous.CompletedAt is null || time > previous.CompletedAt))
                    entries[key] = row;
            }
            if (record.CompletedTaskSnapshots.Count > 0)
            {
                for (var index = 0; index < record.CompletedTaskSnapshots.Count; index++)
                {
                    var snapshot = record.CompletedTaskSnapshots[index];
                    Add(snapshot.TaskId, snapshot.TaskNameSnapshot, snapshot.CompletedAtUtc?.LocalDateTime, index);
                }
            }
            else
            {
                var count = Math.Max(record.CompletedTaskIds.Count, record.CompletedTaskNames.Count);
                for (var index = 0; index < count; index++)
                    Add(record.CompletedTaskIds.ElementAtOrDefault(index), record.CompletedTaskNames.ElementAtOrDefault(index),
                        record.CompletedTaskTimes.ElementAtOrDefault(index), index);
            }
        }
        return entries.Values.ToArray();
    }

    private void RefreshCompletedTasks(bool resetPage)
    {
        CompletedGoalOptions = new[] { new CompletedGoalOption(null, "全部目标") }
            .Concat(_periodCompletedTasks.GroupBy(task => task.GoalId)
                .Select(group => new CompletedGoalOption(group.Key, group.First().GoalName))
                .OrderBy(option => option.Label, StringComparer.CurrentCulture).ThenBy(option => option.GoalId, StringComparer.Ordinal)).ToArray();
        if (!CompletedGoalOptions.Any(option => option.GoalId == _completedGoalId)) _completedGoalId = null;
        var filtered = _periodCompletedTasks.Where(task => _completedGoalId is null || task.GoalId == _completedGoalId);
        var knownFirst = filtered.OrderBy(task => task.CompletedAt is null);
        _filteredCompletedTasks = (_completedNewestFirst ? knownFirst.ThenByDescending(task => task.CompletedAt) : knownFirst.ThenBy(task => task.CompletedAt))
            .ThenBy(task => task.Key, StringComparer.Ordinal).ToArray();
        _completedTasksPage = resetPage ? 1 : Math.Clamp(_completedTasksPage, 1, CompletedTasksPageCount);
        RefreshCompletedTasksPage();
    }

    private void SetCompletedTasksPage(int page)
    {
        if (page < 1 || page > CompletedTasksPageCount || page == _completedTasksPage) return;
        _completedTasksPage = page;
        RefreshCompletedTasksPage();
        NotifyCompletedTasks();
    }
    private void RefreshCompletedTasksPage()
    {
        CompletedTasksPageItems = _filteredCompletedTasks.Skip((CompletedTasksPage - 1) * CompletedTasksPageSize).Take(CompletedTasksPageSize).ToArray();
        var pages = new SortedSet<int> { 1, CompletedTasksPageCount };
        if (CompletedTasksPageCount <= 7) pages.UnionWith(Enumerable.Range(1, CompletedTasksPageCount));
        else
        {
            var start = CompletedTasksPage <= 4 ? 2 : CompletedTasksPage >= CompletedTasksPageCount - 3 ? CompletedTasksPageCount - 4 : CompletedTasksPage - 1;
            var end = CompletedTasksPage <= 4 ? 5 : CompletedTasksPage >= CompletedTasksPageCount - 3 ? CompletedTasksPageCount - 1 : CompletedTasksPage + 1;
            pages.UnionWith(Enumerable.Range(start, end - start + 1));
        }
        var options = new List<CompletedTaskPageOption>();
        var previous = 0;
        foreach (var page in pages)
        {
            if (previous > 0 && page - previous > 1) options.Add(new(null, false));
            options.Add(new(page, page == CompletedTasksPage)); previous = page;
        }
        CompletedTasksPageOptions = options;
        _previousCompletedTasksPage.NotifyCanExecuteChanged();
        _nextCompletedTasksPage.NotifyCanExecuteChanged();
        _goToCompletedTasksPage.NotifyCanExecuteChanged();
    }
    private void NotifyCompletedTasks()
    {
        foreach (var name in new[] { nameof(CompletedGoalOptions), nameof(SelectedCompletedGoal), nameof(SelectedCompletedSort),
            nameof(CompletedTasksPage), nameof(CompletedTasksPageCount), nameof(FilteredCompletedTaskCount), nameof(HasCompletedTaskResults),
            nameof(CompletedTasksListTitle), nameof(CompletedTasksTotalDisplay), nameof(CompletedTasksPageItems), nameof(CompletedTasksPageOptions) })
            OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}

public sealed record OverviewCompletedTaskEntry(string Key, string? TaskId, string Name, string GoalId, string GoalName, string GoalIconSource, DateTime? CompletedAt)
{
    public string DateDisplay => CompletedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
    public bool HasGoal => GoalId != "goal-unassigned";
}
public sealed record CompletedGoalOption(string? GoalId, string Label) { public override string ToString() => Label; }
public sealed record CompletedSortOption(bool NewestFirst, string Label) { public override string ToString() => "按完成时间"; }
public sealed record CompletedTaskPageOption(int? PageNumber, bool IsCurrent)
{
    public string Label => PageNumber?.ToString(CultureInfo.InvariantCulture) ?? "…";
    public bool IsPage => PageNumber.HasValue;
}
