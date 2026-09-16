using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalTasksViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> _now;
    private IReadOnlyList<LocalTaskDto> _snapshot = [];
    private FocusTargetViewModel? _target;
    private bool _isOpen;
    private bool _isCompletedTab;
    private bool _isCreating;
    private string _draftName = string.Empty;
    private string? _errorMessage;
    private Task<bool>? _draftCommit;
    private int _draftRevision;
    private readonly HashSet<string> _completingTasks = [];

    public GoalTasksViewModel(Func<DateTimeOffset>? nowProvider = null)
    {
        _now = nowProvider ?? (() => DateTimeOffset.UtcNow);
        OpenCommand = new RelayCommand<object>(async _ => await OpenAsync(), _ => _target is not null);
        OpenCompletedCommand = new RelayCommand<object>(async _ => await OpenAsync(true), _ => _target is not null);
        CloseCommand = new RelayCommand<object>(async _ => await CloseAsync());
        SelectPendingTabCommand = new RelayCommand<object>(async _ => await SelectTabAsync(false));
        SelectCompletedTabCommand = new RelayCommand<object>(async _ => await SelectTabAsync(true));
        NewTaskCommand = new RelayCommand<object>(_ => BeginCreation(), _ => _target is not null && !IsCompletedTab);
        CompleteTaskCommand = new RelayCommand<FocusTaskViewModel>(async task => { if (task is not null) await CompleteTaskAsync(task); });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DraftFocusRequested;
    public Func<Task>? RefreshTasksAsync { get; set; }
    public Func<LocalTaskDto, bool, Task<LocalTaskDto?>>? PersistTaskAsync { get; set; }
    public ICommand OpenCommand { get; }
    public ICommand OpenCompletedCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectPendingTabCommand { get; }
    public ICommand SelectCompletedTabCommand { get; }
    public ICommand NewTaskCommand { get; }
    public ICommand CompleteTaskCommand { get; }
    public string? GoalId => _target?.TargetId;
    public bool IsOpen { get => _isOpen; private set => SetField(ref _isOpen, value); }
    public bool IsCompletedTab { get => _isCompletedTab; private set { if (SetField(ref _isCompletedTab, value)) { Notify(nameof(IsPendingTab)); Notify(nameof(CanCreateTask)); } } }
    public bool IsPendingTab => !IsCompletedTab;
    public bool CanCreateTask => IsPendingTab && _target is not null;
    public bool IsCreating { get => _isCreating; private set { if (SetField(ref _isCreating, value)) Notify(nameof(ShowPendingEmptyState)); } }
    public string DraftName { get => _draftName; set => SetField(ref _draftName, value); }
    public string? ErrorMessage { get => _errorMessage; private set { if (SetField(ref _errorMessage, value)) Notify(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public IReadOnlyList<FocusTaskViewModel> PendingTasks => _target?.Tasks.Where(task => !task.IsCompleted).ToArray() ?? [];
    public int PendingCount => _target?.Tasks.Count(task => !task.IsCompleted) ?? 0;
    public int CompletedCount => _target?.Tasks.Count(task => task.IsCompleted) ?? 0;
    public string PendingTabTitle => $"未完成 {PendingCount}";
    public string CompletedTabTitle => $"已完成 {CompletedCount}";
    public bool HasPendingTasks => PendingCount > 0;
    public bool HasCompletedTasks => CompletedCount > 0;
    public bool ShowPendingEmptyState => !HasPendingTasks && !IsCreating;
    public IReadOnlyList<GoalTaskDateGroupViewModel> CompletedGroups
    {
        get
        {
            var groups = (_target?.Tasks.Where(task => task.IsCompleted) ?? [])
                .GroupBy(task => task.CompletedAtUtc?.ToLocalTime().Date)
                .OrderByDescending(group => group.Key)
                .ToArray();
            return groups.Select((group, index) => new GoalTaskDateGroupViewModel(
                group.Key, group.OrderByDescending(task => task.CompletedAtUtc).ToArray(),
                _now().ToLocalTime().Date, index == groups.Length - 1)).ToArray();
        }
    }

    public void ApplyState(GoalOverviewItemViewModel? goal, IReadOnlyList<LocalTaskDto> tasks)
    {
        _snapshot = tasks;
        if (GoalId != goal?.GoalId)
        {
            CancelCreation();
            IsOpen = false;
            _target = goal is null ? null : new FocusTargetViewModel(goal.Name, targetId: goal.GoalId);
            Notify(nameof(GoalId));
        }
        if (_target is not null && goal is not null) _target.ApplyName(goal.Name);
        ProjectTasks();
        Notify(nameof(CanCreateTask));
        ((RelayCommand<object>)OpenCommand).NotifyCanExecuteChanged();
        ((RelayCommand<object>)OpenCompletedCommand).NotifyCanExecuteChanged();
        ((RelayCommand<object>)NewTaskCommand).NotifyCanExecuteChanged();
    }

    private void ProjectTasks()
    {
        if (_target is not null)
        {
            var ordered = _snapshot.Where(task => task.TargetId == GoalId).OrderBy(task => task.SortOrder).ToArray();
            var existing = _target.Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
            foreach (var removed in _target.Tasks.Where(task => ordered.All(item => item.TaskId != task.TaskId)).ToArray())
                _target.RemoveTask(removed);
            for (var index = 0; index < ordered.Length; index++)
            {
                var source = ordered[index];
                if (existing.TryGetValue(source.TaskId, out var task))
                {
                    task.ApplyName(source.Name);
                    task.ApplyCreatedAt(source.CreatedAtUtc);
                    task.ApplyCompletion(source.IsCompleted, source.CompletedAtUtc);
                }
                else task = _target.AddTask(source.TaskId, source.Name, source.IsCompleted, createdAtUtc: source.CreatedAtUtc, completedAtUtc: source.CompletedAtUtc);
                var oldIndex = _target.Tasks.IndexOf(task);
                if (oldIndex != index) _target.Tasks.Move(oldIndex, index);
            }
        }
        NotifyViews();
    }

    public async Task OpenAsync(bool completed = false)
    {
        if (_target is null) return;
        IsCompletedTab = completed;
        ErrorMessage = null;
        IsOpen = true;
        if (RefreshTasksAsync is not null) await RefreshTasksAsync();
        NotifyViews(); // Recompute relative dates even when no tasks changed overnight.
    }

    public async Task<bool> CloseAsync()
    {
        if (!await CommitCreationAsync()) return false;
        IsOpen = false;
        return true;
    }

    public async Task SelectTabAsync(bool completed)
    {
        if (!await CommitCreationAsync()) return;
        IsCompletedTab = completed;
        ((RelayCommand<object>)NewTaskCommand).NotifyCanExecuteChanged();
    }

    public void BeginCreation()
    {
        if (!CanCreateTask) return;
        if (!IsCreating && _draftCommit is null)
        {
            _draftRevision++;
            DraftName = string.Empty;
            ErrorMessage = null;
            IsCreating = true;
        }
        if (IsCreating) DraftFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void CancelCreation()
    {
        _draftRevision++;
        IsCreating = false;
        DraftName = string.Empty;
        ErrorMessage = null;
    }

    public Task<bool> CommitCreationAsync()
    {
        if (_draftCommit is not null) return _draftCommit;
        if (!IsCreating) return Task.FromResult(true);
        var name = DraftName.Trim();
        if (name.Length == 0 || GoalId is null) { CancelCreation(); return Task.FromResult(true); }
        var now = _now().ToUniversalTime();
        var task = new LocalTaskDto(Guid.NewGuid().ToString("N"), GoalId, name, false, 0, now, now);
        return CommitDraftAsync(task, _draftRevision);
    }

    private async Task<bool> CommitDraftAsync(LocalTaskDto task, int revision)
    {
        // Assign before awaiting so Enter, blur and close share one save operation.
        var completion = new TaskCompletionSource<bool>();
        _draftCommit = completion.Task;
        try
        {
            var saved = await SaveAsync(task, insertAtTop: true);
            if (saved is not null)
            {
                if (_draftRevision == revision) CancelCreation();
                completion.SetResult(true);
                return true;
            }
            completion.SetResult(false);
            return false;
        }
        finally { _draftCommit = null; }
    }

    public async Task<bool> CompleteTaskAsync(FocusTaskViewModel task)
    {
        if (task.TargetId != GoalId || task.IsCompleted || !_completingTasks.Add(task.TaskId)) return false;
        try
        {
            if (!await CommitCreationAsync() || task.TargetId != GoalId) return false;
            var existing = _snapshot.FirstOrDefault(item => item.TaskId == task.TaskId && item.TargetId == task.TargetId);
            if (existing is null || existing.IsCompleted) return false;
            var now = _now().ToUniversalTime();
            return await SaveAsync(existing with { IsCompleted = true, CompletedAtUtc = now, UpdatedAtUtc = now }, false) is not null;
        }
        finally { _completingTasks.Remove(task.TaskId); }
    }

    private async Task<LocalTaskDto?> SaveAsync(LocalTaskDto task, bool insertAtTop)
    {
        ErrorMessage = null;
        LocalTaskDto? saved;
        try { saved = PersistTaskAsync is null ? null : await PersistTaskAsync(task, insertAtTop); }
        catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException or IOException)
        {
            saved = null;
        }
        if (saved is null)
        {
            if (GoalId == task.TargetId) ErrorMessage = "任务保存失败，请重试。";
            return null;
        }
        // The service also broadcasts a full snapshot. This upsert provides immediate UI feedback.
        var tasks = _snapshot.Where(item => item.TaskId != saved.TaskId).ToList();
        if (insertAtTop)
        {
            var ownTasks = tasks.Where(item => item.TargetId == saved.TargetId).OrderBy(item => item.SortOrder).ToArray();
            tasks.RemoveAll(item => item.TargetId == saved.TargetId);
            tasks.AddRange(ownTasks.Select((item, index) => item with { SortOrder = index + 1 }));
        }
        tasks.Add(saved);
        _snapshot = tasks;
        ProjectTasks();
        return saved;
    }

    private void NotifyViews()
    {
        foreach (var name in new[] { nameof(PendingTasks), nameof(CompletedGroups), nameof(PendingCount), nameof(CompletedCount), nameof(PendingTabTitle), nameof(CompletedTabTitle), nameof(HasPendingTasks), nameof(HasCompletedTasks), nameof(ShowPendingEmptyState) }) Notify(name);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GoalTaskDateGroupViewModel(DateTime? date, IReadOnlyList<FocusTaskViewModel> tasks, DateTime today, bool isLast)
{
    public IReadOnlyList<FocusTaskViewModel> Tasks => tasks;
    public DateTime? Date => date;
    public bool IsLast => isLast;
    public string Title => date is null ? "完成日期未知" : date == today ? "今天" : date == today.AddDays(-1) ? "昨天" : FormatDate(date.Value);
    public string Subtitle => date is null ? $"{tasks.Count}项" : $"{(date == today || date == today.AddDays(-1) ? FormatDate(date.Value) + " " : "")}周{"日一二三四五六"[(int)date.Value.DayOfWeek]} · {tasks.Count}项";
    private string FormatDate(DateTime value) => value.Year == today.Year ? $"{value:M月d日}" : $"{value:yyyy年M月d日}";
}
