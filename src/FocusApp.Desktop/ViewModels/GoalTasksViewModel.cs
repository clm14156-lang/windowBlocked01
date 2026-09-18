using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalTasksViewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<TimeSpan, Task> _completionAnimationDelay;
    private readonly ObservableCollection<FocusTaskViewModel> _pendingTasks = [];
    private readonly ReadOnlyObservableCollection<FocusTaskViewModel> _readOnlyPendingTasks;
    private readonly ObservableCollection<GoalTaskDateGroupViewModel> _completedGroups = [];
    private readonly ReadOnlyObservableCollection<GoalTaskDateGroupViewModel> _readOnlyCompletedGroups;
    private IReadOnlyList<LocalTaskDto> _snapshot = [];
    private FocusTargetViewModel? _target;
    private bool _isOpen;
    private bool _isCreating;
    private string _draftName = string.Empty;
    private string? _errorMessage;
    private Task<bool>? _draftCommit;
    private int _draftRevision;
    private readonly HashSet<string> _completingTasks = [];
    private bool _isReorderingTasks;
    private readonly HashSet<string> _deletingTasks = [];

    public GoalTasksViewModel(
        Func<DateTimeOffset>? nowProvider = null,
        Func<TimeSpan, Task>? completionAnimationDelay = null)
    {
        _now = nowProvider ?? (() => DateTimeOffset.UtcNow);
        _completionAnimationDelay = completionAnimationDelay ?? Task.Delay;
        _readOnlyPendingTasks = new ReadOnlyObservableCollection<FocusTaskViewModel>(_pendingTasks);
        _readOnlyCompletedGroups = new ReadOnlyObservableCollection<GoalTaskDateGroupViewModel>(_completedGroups);
        OpenCompletedCommand = new RelayCommand<object>(async _ => await OpenCompletedAsync(), _ => _target is not null);
        CloseCommand = new RelayCommand<object>(async _ => await CloseAsync());
        NewTaskCommand = new RelayCommand<object>(_ => BeginCreation(), _ => _target is not null);
        CompleteTaskCommand = new RelayCommand<FocusTaskViewModel>(async task => { if (task is not null) await CompleteTaskAsync(task); });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DraftFocusRequested;
    public Func<Task>? RefreshTasksAsync { get; set; }
    public Func<LocalTaskDto, bool, Task<LocalTaskDto?>>? PersistTaskAsync { get; set; }
    public Func<string, string, Task<bool>>? PersistCompletedTaskDeletionAsync { get; set; }
    public Func<string, IReadOnlyList<string>, Task<bool>>? PersistPendingTaskOrderAsync { get; set; }
    public ICommand OpenCompletedCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand NewTaskCommand { get; }
    public ICommand CompleteTaskCommand { get; }
    public string? GoalId => _target?.TargetId;
    public bool IsOpen { get => _isOpen; private set => SetField(ref _isOpen, value); }
    public bool CanCreateTask => _target is not null;
    public bool IsCreating { get => _isCreating; private set { if (SetField(ref _isCreating, value)) Notify(nameof(ShowPendingEmptyState)); } }
    public string DraftName { get => _draftName; set => SetField(ref _draftName, value); }
    public string? ErrorMessage { get => _errorMessage; private set { if (SetField(ref _errorMessage, value)) Notify(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public IReadOnlyList<FocusTaskViewModel> PendingTasks => _readOnlyPendingTasks;
    public int PendingCount => _pendingTasks.Count;
    public int CompletedCount => CompletedTasks.Count;
    public int TodayCompletedCount
    {
        get
        {
            var today = _now().ToLocalTime().Date;
            return CompletedTasks.Count(task => task.CompletedAtUtc?.ToLocalTime().Date == today);
        }
    }
    public string TodayCompletedSummary => $"今日已完成 {TodayCompletedCount} 项";
    public bool HasPendingTasks => PendingCount > 0;
    public bool HasCompletedTasks => CompletedCount > 0;
    public bool ShowPendingEmptyState => !HasPendingTasks && !IsCreating;
    public IReadOnlyList<GoalTaskDateGroupViewModel> CompletedGroups => _readOnlyCompletedGroups;

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
                    // The service snapshot can arrive before the completion
                    // animation finishes. Keep this one row pending until its
                    // fade/collapse completes so the list never removes it early.
                    var completionIsAnimating = task.IsCompleting && !task.IsCompleted && source.IsCompleted;
                    var uncompletionIsAnimating = task.IsUncompleting && task.IsCompleted && !source.IsCompleted;
                    if (!completionIsAnimating && !uncompletionIsAnimating)
                    {
                        task.ApplyCompletion(source.IsCompleted, source.CompletedAtUtc);
                    }
                }
                else task = _target.AddTask(source.TaskId, source.Name, source.IsCompleted, createdAtUtc: source.CreatedAtUtc, completedAtUtc: source.CompletedAtUtc);
                var oldIndex = _target.Tasks.IndexOf(task);
                if (oldIndex != index) _target.Tasks.Move(oldIndex, index);
            }
        }
        SynchronizeTaskProjections();
        UpdatePendingTaskPriorities();
        NotifyViews();
    }

    public async Task OpenCompletedAsync()
    {
        if (_target is null) return;
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
        task.IsCompleting = true;
        try
        {
            if (!await CommitCreationAsync() || task.TargetId != GoalId) return false;
            var existing = _snapshot.FirstOrDefault(item => item.TaskId == task.TaskId && item.TargetId == task.TargetId);
            if (existing is null || existing.IsCompleted) return false;
            var now = _now().ToUniversalTime();
            var animation = RunCompletionAnimationAsync(task);
            var persistence = SaveAsync(
                existing with { IsCompleted = true, CompletedAtUtc = now, UpdatedAtUtc = now },
                false);
            await animation;
            var saved = await persistence;
            if (saved is null || task.TargetId != GoalId)
            {
                return false;
            }

            task.ApplyCompletion(true, saved.CompletedAtUtc);
            ProjectTasks();
            return true;
        }
        finally
        {
            task.IsCompletionExiting = false;
            task.IsCompletionStyled = false;
            task.IsCompleting = false;
            _completingTasks.Remove(task.TaskId);
        }
    }

    private async Task RunCompletionAnimationAsync(FocusTaskViewModel task)
    {
        await _completionAnimationDelay(TimeSpan.FromMilliseconds(120));
        if (!task.IsCompleting) return;
        task.IsCompletionStyled = true;

        await _completionAnimationDelay(TimeSpan.FromMilliseconds(160));
        if (!task.IsCompleting) return;
        task.IsCompletionExiting = true;

        await _completionAnimationDelay(TimeSpan.FromMilliseconds(200));
    }

    public async Task<bool> UncompleteTaskAsync(FocusTaskViewModel task)
    {
        if (task.TargetId != GoalId || !task.IsCompleted || !_completingTasks.Add(task.TaskId)) return false;
        task.IsUncompleting = true;
        try
        {
            if (!await CommitCreationAsync() || task.TargetId != GoalId) return false;
            var existing = _snapshot.FirstOrDefault(item => item.TaskId == task.TaskId && item.TargetId == task.TargetId);
            if (existing is null || !existing.IsCompleted) return false;
            var now = _now().ToUniversalTime();
            var animation = RunUncompletionAnimationAsync(task);
            var persistence = SaveAsync(
                existing with { IsCompleted = false, CompletedAtUtc = null, UpdatedAtUtc = now },
                false);
            await animation;
            var saved = await persistence;
            if (saved is null || task.TargetId != GoalId)
            {
                return false;
            }

            task.ApplyCompletion(false, null);
            ProjectTasks();
            return true;
        }
        finally
        {
            task.IsUncompletionExiting = false;
            task.IsUncompletionRestored = false;
            task.IsUncompleting = false;
            _completingTasks.Remove(task.TaskId);
        }
    }

    private async Task RunUncompletionAnimationAsync(FocusTaskViewModel task)
    {
        await _completionAnimationDelay(TimeSpan.FromMilliseconds(120));
        if (!task.IsUncompleting) return;
        task.IsUncompletionRestored = true;

        await _completionAnimationDelay(TimeSpan.FromMilliseconds(160));
        if (!task.IsUncompleting) return;
        task.IsUncompletionExiting = true;

        await _completionAnimationDelay(TimeSpan.FromMilliseconds(200));
    }

    public void RefreshDateSensitiveViews()
    {
        SynchronizeCompletedGroups();
        NotifyViews();
    }

    public async Task<bool> DeleteCompletedTaskAsync(FocusTaskViewModel task)
    {
        if (task.TargetId != GoalId || !task.IsCompleted ||
            !_snapshot.Any(item => item.TaskId == task.TaskId && item.TargetId == task.TargetId && item.IsCompleted) ||
            !_deletingTasks.Add(task.TaskId)) return false;
        ErrorMessage = null;
        try
        {
            var deleted = false;
            try
            {
                deleted = PersistCompletedTaskDeletionAsync is not null &&
                    await PersistCompletedTaskDeletionAsync(task.TargetId, task.TaskId);
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException or IOException) { }
            if (!deleted)
            {
                if (GoalId == task.TargetId) ErrorMessage = "任务删除失败，请重试。";
                return false;
            }
            _snapshot = _snapshot.Where(item => item.TaskId != task.TaskId || item.TargetId != task.TargetId).ToArray();
            ProjectTasks();
            return true;
        }
        finally { _deletingTasks.Remove(task.TaskId); }
    }

    public async Task<bool> MovePendingTaskAsync(
        FocusTaskViewModel task,
        FocusTaskViewModel target,
        bool insertAfter)
    {
        if (_isReorderingTasks || GoalId is not { } goalId ||
            task.TargetId != goalId || target.TargetId != goalId ||
            task.IsCompleted || target.IsCompleted || ReferenceEquals(task, target))
        {
            return false;
        }

        var previousOrder = PendingTasks.Select(item => item.TaskId).ToArray();
        var reordered = PendingTasks.ToList();
        var oldIndex = reordered.IndexOf(task);
        if (oldIndex < 0 || !reordered.Remove(task)) return false;
        var targetIndex = reordered.IndexOf(target);
        if (targetIndex < 0) return false;
        reordered.Insert(targetIndex + (insertAfter ? 1 : 0), task);
        var reorderedIds = reordered.Select(item => item.TaskId).ToArray();
        if (previousOrder.SequenceEqual(reorderedIds, StringComparer.Ordinal)) return false;

        _isReorderingTasks = true;
        ErrorMessage = null;
        ApplyPendingOrder(reorderedIds);
        try
        {
            bool persisted;
            try
            {
                persisted = PersistPendingTaskOrderAsync is not null &&
                    await PersistPendingTaskOrderAsync(goalId, reorderedIds);
            }
            catch (Exception exception) when (exception is IpcConnectionException or IpcRemoteException or InvalidOperationException or IOException)
            {
                persisted = false;
            }

            if (!persisted)
            {
                if (GoalId == goalId) ProjectTasks();
                ErrorMessage = "任务排序失败，请重试。";
                return false;
            }

            if (GoalId == goalId)
            {
                CommitPendingOrderToSnapshot(goalId, reorderedIds);
                ProjectTasks();
            }
            return true;
        }
        finally
        {
            _isReorderingTasks = false;
        }
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

    private void ApplyPendingOrder(IReadOnlyList<string> orderedPendingTaskIds)
    {
        if (_target is null) return;
        var pendingById = _target.Tasks
            .Where(item => !item.IsCompleted)
            .ToDictionary(item => item.TaskId, StringComparer.Ordinal);
        if (orderedPendingTaskIds.Any(taskId => !pendingById.ContainsKey(taskId))) return;

        var orderedPending = orderedPendingTaskIds.Select(taskId => pendingById[taskId]).ToArray();
        var desired = _target.Tasks.ToArray();
        var pendingIndex = 0;
        for (var index = 0; index < desired.Length; index++)
        {
            if (!desired[index].IsCompleted) desired[index] = orderedPending[pendingIndex++];
        }

        for (var index = 0; index < desired.Length; index++)
        {
            var oldIndex = _target.Tasks.IndexOf(desired[index]);
            if (oldIndex != index) _target.Tasks.Move(oldIndex, index);
        }
        SynchronizePendingTasks();
        UpdatePendingTaskPriorities();
        NotifyViews();
    }

    private void SynchronizeTaskProjections()
    {
        // Remove from the completed projection before adding to pending so an
        // uncompleted task has one clear local move between stable sources.
        SynchronizeCompletedGroups();
        SynchronizePendingTasks();
    }

    private void SynchronizePendingTasks()
    {
        var desired = _target?.Tasks.Where(task => !task.IsCompleted).ToArray() ?? [];
        SynchronizeCollection(_pendingTasks, desired);
    }

    private void SynchronizeCompletedGroups()
    {
        var today = _now().ToLocalTime().Date;
        var desiredGroups = CompletedTasks
            .GroupBy(task => task.CompletedAtUtc?.ToLocalTime().Date)
            .OrderByDescending(group => group.Key)
            .Select(group => new
            {
                Date = group.Key,
                Tasks = group.OrderByDescending(task => task.CompletedAtUtc).ToArray()
            })
            .ToArray();

        for (var index = _completedGroups.Count - 1; index >= 0; index--)
        {
            if (!desiredGroups.Any(group => group.Date == _completedGroups[index].Date))
            {
                _completedGroups.RemoveAt(index);
            }
        }

        for (var index = 0; index < desiredGroups.Length; index++)
        {
            var desired = desiredGroups[index];
            var group = _completedGroups.FirstOrDefault(item => item.Date == desired.Date);
            if (group is null)
            {
                group = new GoalTaskDateGroupViewModel(desired.Date, desired.Tasks, today, index == desiredGroups.Length - 1);
                _completedGroups.Insert(index, group);
            }
            else
            {
                group.Apply(desired.Tasks, today, index == desiredGroups.Length - 1);
                var oldIndex = _completedGroups.IndexOf(group);
                if (oldIndex != index)
                {
                    _completedGroups.Move(oldIndex, index);
                }
            }
        }
    }

    internal static void SynchronizeCollection(
        ObservableCollection<FocusTaskViewModel> target,
        IReadOnlyList<FocusTaskViewModel> desired)
    {
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index]))
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var task = desired[index];
            var oldIndex = target.IndexOf(task);
            if (oldIndex < 0)
            {
                target.Insert(index, task);
            }
            else if (oldIndex != index)
            {
                target.Move(oldIndex, index);
            }
        }
    }

    private void CommitPendingOrderToSnapshot(string goalId, IReadOnlyList<string> orderedPendingTaskIds)
    {
        var ownTasks = _snapshot
            .Where(item => item.TargetId == goalId)
            .OrderBy(item => item.SortOrder)
            .ToList();
        var pendingById = ownTasks
            .Where(item => !item.IsCompleted)
            .ToDictionary(item => item.TaskId, StringComparer.Ordinal);
        if (orderedPendingTaskIds.Any(taskId => !pendingById.ContainsKey(taskId))) return;

        var orderedPending = orderedPendingTaskIds.Select(taskId => pendingById[taskId]).ToArray();
        var pendingIndex = 0;
        for (var index = 0; index < ownTasks.Count; index++)
        {
            var task = ownTasks[index];
            if (!task.IsCompleted) task = orderedPending[pendingIndex++];
            ownTasks[index] = task with { SortOrder = index };
        }

        _snapshot = _snapshot.Where(item => item.TargetId != goalId).Concat(ownTasks).ToArray();
    }

    private void UpdatePendingTaskPriorities()
    {
        if (_target is null) return;
        var rank = 0;
        foreach (var task in _target.Tasks)
        {
            task.ListPriorityRank = !task.IsCompleted && rank < 3 ? ++rank : 0;
        }
    }

    private void NotifyViews()
    {
        foreach (var name in new[]
                 {
                     nameof(PendingCount), nameof(CompletedCount),
                     nameof(TodayCompletedCount), nameof(TodayCompletedSummary), nameof(HasPendingTasks),
                     nameof(HasCompletedTasks), nameof(ShowPendingEmptyState)
                 })
        {
            Notify(name);
        }
    }

    private IReadOnlyList<FocusTaskViewModel> CompletedTasks =>
        _target?.Tasks.Where(task => task.IsCompleted).ToArray() ?? [];

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GoalTaskDateGroupViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<FocusTaskViewModel> _tasks = [];
    private readonly ReadOnlyObservableCollection<FocusTaskViewModel> _readOnlyTasks;
    private DateTime _today;
    private bool _isLast;

    public GoalTaskDateGroupViewModel(
        DateTime? date,
        IReadOnlyList<FocusTaskViewModel> tasks,
        DateTime today,
        bool isLast)
    {
        Date = date;
        _today = today;
        _isLast = isLast;
        _readOnlyTasks = new ReadOnlyObservableCollection<FocusTaskViewModel>(_tasks);
        Apply(tasks, today, isLast);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<FocusTaskViewModel> Tasks => _readOnlyTasks;
    public DateTime? Date { get; }
    public bool IsLast => _isLast;
    public string Title => Date is null ? "完成日期未知" : Date == _today ? "今天" : Date == _today.AddDays(-1) ? "昨天" : FormatDate(Date.Value);
    public string Subtitle => $"{_tasks.Count}项";

    internal void Apply(IReadOnlyList<FocusTaskViewModel> tasks, DateTime today, bool isLast)
    {
        var todayChanged = _today != today;
        var isLastChanged = _isLast != isLast;
        _today = today;
        _isLast = isLast;
        GoalTasksViewModel.SynchronizeCollection(_tasks, tasks);
        if (todayChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        if (isLastChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLast)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
    }

    private string FormatDate(DateTime value) => value.Year == _today.Year ? $"{value:M月d日}" : $"{value:yyyy年M月d日}";
}
