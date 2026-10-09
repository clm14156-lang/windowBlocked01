using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalCompletedTasksViewModel : INotifyPropertyChanged
{
    private string? _goalId;
    private IReadOnlyList<LocalTaskDto> _tasks = [];
    private bool _isOpen, _isBusy;
    private string _searchText = string.Empty, _selectedSort = "最近完成", _errorText = string.Empty;
    private readonly RelayCommand<object> _open;
    private readonly RelayCommand<GoalCompletedTaskEntryViewModel> _restore, _delete;
    public GoalCompletedTasksViewModel()
    {
        _open = new(_ => Open(), _ => _goalId is not null);
        OpenCommand = _open;
        CloseCommand = new RelayCommand<object>(_ => IsOpen = false);
        SelectSortCommand = new RelayCommand<string>(value => { if (value is not null) SelectedSort = value; });
        _restore = new(async entry => { if (entry is not null) await MutateAsync(entry, false); }, entry => entry is not null && !IsBusy);
        _delete = new(async entry => { if (entry is not null) await MutateAsync(entry, true); }, entry => entry is not null && !IsBusy);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<GoalCompletedTaskDateViewModel> Days { get; } = [];
    public IReadOnlyList<string> SortOptions { get; } = ["最近完成", "最早完成", "完成最多"];
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectSortCommand { get; }
    public Func<LocalTaskDto, bool, Task<bool>>? MutationHandler { get; set; }
    public bool IsOpen { get => _isOpen; private set => Set(ref _isOpen, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (Set(ref _isBusy, value)) { _restore.NotifyCanExecuteChanged(); _delete.NotifyCanExecuteChanged(); } }
    }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value ?? string.Empty)) Refresh(resetExpansion: true); } }
    public string SelectedSort { get => _selectedSort; set { if (SortOptions.Contains(value) && Set(ref _selectedSort, value)) Refresh(); } }
    public string ErrorText { get => _errorText; private set { if (Set(ref _errorText, value)) Changed(nameof(HasError)); } }
    public bool HasError => ErrorText.Length > 0;
    public int TotalCount => _tasks.Count;
    public bool HasTasks => TotalCount > 0;
    public bool HasResults => Days.Count > 0;
    public string SummaryDisplay => $"已完成 {TotalCount} 项";
    public string EmptyText => string.IsNullOrWhiteSpace(SearchText) ? "暂无已完成任务" : "没有找到匹配的任务";

    public void ApplyState(GoalOverviewItemViewModel? goal, IReadOnlyList<LocalTaskDto> tasks)
    {
        var changedGoal = _goalId != goal?.GoalId;
        _goalId = goal?.GoalId;
        _tasks = tasks.Where(task => task.TargetId == _goalId &&
                TaskCompletionPolicy.IsCompleted(task.IsCompleted, task.SubTasks.Select(child => child.IsCompleted)))
            .GroupBy(task => task.TaskId).Select(group => group.First()).ToArray();
        if (changedGoal)
        {
            IsOpen = false;
            _searchText = string.Empty; _selectedSort = "最近完成"; ErrorText = string.Empty;
            Changed(nameof(SearchText)); Changed(nameof(SelectedSort));
        }
        Refresh(changedGoal);
        Changed(nameof(TotalCount)); Changed(nameof(HasTasks)); Changed(nameof(SummaryDisplay));
        _open.NotifyCanExecuteChanged();
    }
    private void Open()
    {
        if (_goalId is null) return;
        _searchText = string.Empty; _selectedSort = "最近完成"; ErrorText = string.Empty;
        Changed(nameof(SearchText)); Changed(nameof(SelectedSort)); Refresh(true); IsOpen = true;
    }
    private void Refresh(bool resetExpansion = false)
    {
        var expanded = resetExpansion ? new Dictionary<DateTime, bool>() : Days.ToDictionary(day => day.Date, day => day.IsExpanded);
        var query = SearchText.Trim();
        var groups = _tasks.GroupBy(task => CompletionTime(task).Date)
            .Select(group => new { group.Key, Count = group.Count(), Tasks = group.Where(task => Matches(task, query)).OrderByDescending(CompletionTime).ThenBy(task => task.SortOrder).ToArray() })
            .Where(group => group.Tasks.Length > 0);
        groups = SelectedSort switch
        {
            "最早完成" => groups.OrderBy(group => group.Key),
            "完成最多" => groups.OrderByDescending(group => group.Count).ThenByDescending(group => group.Key),
            _ => groups.OrderByDescending(group => group.Key)
        };
        var latest = _tasks.Count == 0 ? (DateTime?)null : _tasks.Max(task => CompletionTime(task).Date);
        Days.Clear();
        foreach (var group in groups)
            Days.Add(new(group.Key, group.Tasks.Select(task => new GoalCompletedTaskEntryViewModel(task, _restore, _delete)).ToArray(),
                query.Length > 0 || (expanded.TryGetValue(group.Key, out var open) ? open : group.Key == latest)));
        Changed(nameof(HasResults)); Changed(nameof(EmptyText));
    }
    internal static DateTime CompletionTime(LocalTaskDto task) => (task.CompletedAtUtc ?? task.UpdatedAtUtc).LocalDateTime;
    private static bool Matches(LocalTaskDto task, string query) => query.Length == 0 ||
        task.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || task.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        task.SubTasks.Any(child => child.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
    public async Task<bool> MutateAsync(GoalCompletedTaskEntryViewModel entry, bool delete)
    {
        if (IsBusy || !_tasks.Any(task => task.TaskId == entry.Source.TaskId)) return false;
        IsBusy = true; ErrorText = string.Empty;
        try
        {
            if (MutationHandler is null || !await MutationHandler(entry.Source, delete))
            { ErrorText = delete ? "删除失败，请稍后重试" : "恢复失败，请稍后重试"; return false; }
            _tasks = _tasks.Where(task => task.TaskId != entry.Source.TaskId).ToArray();
            Refresh(); Changed(nameof(TotalCount)); Changed(nameof(HasTasks)); Changed(nameof(SummaryDisplay));
            return true;
        }
        catch (Exception) { ErrorText = "操作未保存，请稍后重试"; return false; }
        finally { IsBusy = false; }
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class GoalCompletedTaskDateViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    public GoalCompletedTaskDateViewModel(DateTime date, IReadOnlyList<GoalCompletedTaskEntryViewModel> tasks, bool expanded)
    { Date = date; Tasks = tasks; _isExpanded = expanded; ToggleCommand = new RelayCommand<object>(_ => IsExpanded = !IsExpanded); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime Date { get; }
    public string DateDisplay => Date.ToString("M月d日");
    public string CountDisplay => $"{Tasks.Count}项";
    public IReadOnlyList<GoalCompletedTaskEntryViewModel> Tasks { get; }
    public ICommand ToggleCommand { get; }
    public bool IsExpanded { get => _isExpanded; private set { _isExpanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); } }
}
public sealed record GoalCompletedTaskEntryViewModel(LocalTaskDto Source, ICommand RestoreCommand, ICommand DeleteCommand)
{
    public string Name => Source.Name;
    public string Description => Source.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public IReadOnlyList<LocalSubTaskDto> SubTasks => Source.SubTasks.OrderBy(child => child.SortOrder).ToArray();
    public bool HasSubTasks => SubTasks.Count > 0;
    public string TimeDisplay => GoalCompletedTasksViewModel.CompletionTime(Source).ToString("HH:mm");
}
