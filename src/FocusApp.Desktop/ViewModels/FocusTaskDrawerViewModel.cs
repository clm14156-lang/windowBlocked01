using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTaskDrawerViewModel : INotifyPropertyChanged
{
    private readonly FocusSessionViewModel _session;
    private readonly RelayCommand<object> _createTaskCommand;
    private readonly RelayCommand<object> _addSubTaskCommand;
    private bool _isOpen;
    private bool _isCreating;
    private bool _isDescriptionExpanded;
    private bool _isSubTasksExpanded;
    private bool _isSubTaskInputVisible;
    private string _draftTitle = string.Empty;
    private string _subTaskInput = string.Empty;
    private FocusTaskViewModel? _draft;
    private FocusTaskViewModel? _editingTask;
    private FocusTaskViewModel? _selectedTask;
    private readonly HashSet<FocusTaskViewModel> _exitingTasks = [];

    public FocusTaskDrawerViewModel(FocusSessionViewModel session)
    {
        _session = session;
        ToggleCommand = new RelayCommand<object>(_ => { if (_session.IsFocusing && _session.HasTarget) IsOpen = !IsOpen; });
        CloseCommand = new RelayCommand<object>(_ => Close());
        AddTaskCommand = new RelayCommand<object>(_ => BeginCreation());
        CancelCreationCommand = new RelayCommand<object>(_ => CancelCreation());
        ExpandDescriptionCommand = new RelayCommand<object>(_ => { if (_draft is not null && HasDraftTitle) IsDescriptionExpanded = true; });
        BeginSubTaskCommand = new RelayCommand<object>(_ =>
        {
            if (_draft is null || !HasDraftTitle || !CanEnterSubTask) return;
            IsSubTasksExpanded = true;
            IsSubTaskInputVisible = true;
        });
        ToggleExpandedCommand = new RelayCommand<FocusTaskViewModel>(task =>
        {
            if (task is null) return;
            if (task == SelectedTask) task.IsExpanded = !task.IsExpanded;
            else SelectTask(task);
        });
        EditTaskCommand = new RelayCommand<FocusTaskViewModel>(BeginTaskEdit);
        DeleteTaskCommand = new RelayCommand<FocusTaskViewModel>(task =>
        {
            if (task == _editingTask) CancelCreation();
            _session.DeleteTaskCommand.Execute(task);
        });
        DeleteSubTaskCommand = new RelayCommand<FocusSubTaskViewModel>(item => { if (item is not null) _draft?.SubTasks.Remove(item); });
        _createTaskCommand = new RelayCommand<object>(_ => CommitCreation(), _ => _draft is not null && !string.IsNullOrWhiteSpace(DraftTitle));
        CreateTaskCommand = _createTaskCommand;
        _addSubTaskCommand = new RelayCommand<object>(_ => AddSubTask(), _ => _draft is not null && _draft.SubTasks.Count < 20 && !string.IsNullOrWhiteSpace(SubTaskInput));
        AddSubTaskCommand = _addSubTaskCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<TaskCompletionPresentationEventArgs>? CompletionAnimationRequested;
    public event EventHandler<FocusTaskViewModel>? CompletionAnimationCancelled;
    public ObservableCollection<FocusTaskViewModel> Tasks { get; } = [];
    public ObservableCollection<FocusTaskViewModel> TodayCompletedTasks { get; } = [];
    public ObservableCollection<FocusTaskViewModel> VisibleTasks { get; } = [];
    public ICommand ToggleCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand AddTaskCommand { get; }
    public ICommand CancelCreationCommand { get; }
    public ICommand ExpandDescriptionCommand { get; }
    public ICommand BeginSubTaskCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand EditTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand CreateTaskCommand { get; }
    public ICommand AddSubTaskCommand { get; }
    public ICommand DeleteSubTaskCommand { get; }
    public ICommand ToggleTaskCompletedCommand => _session.ToggleTaskCompletedCommand;
    public FocusTaskViewModel? SelectedTask => _selectedTask;
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (!Set(ref _isOpen, value)) return;
            if (!value) { FinishCompletionAnimations(); CancelCreation(); SelectTask(null); }
            _session.NotifyTaskDrawerChanged();
        }
    }
    public bool IsCreating { get => _isCreating; private set => Set(ref _isCreating, value); }
    public bool IsEditingTask => _editingTask is not null;
    public string CommitButtonText => IsEditingTask ? "保存修改" : "创建";
    public string DraftTitle
    {
        get => _draftTitle;
        set
        {
            if (!Set(ref _draftTitle, value ?? string.Empty)) return;
            _createTaskCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasDraftTitle));
            RefreshDraftActions();
        }
    }
    public FocusTaskViewModel? Draft => _draft;
    public bool HasDraftTitle => !string.IsNullOrWhiteSpace(DraftTitle);
    public bool IsDescriptionExpanded
    {
        get => _isDescriptionExpanded;
        private set { if (Set(ref _isDescriptionExpanded, value)) RefreshDraftActions(); }
    }
    public bool IsSubTasksExpanded { get => _isSubTasksExpanded; private set => Set(ref _isSubTasksExpanded, value); }
    public bool IsSubTaskInputVisible
    {
        get => _isSubTaskInputVisible;
        private set { if (Set(ref _isSubTaskInputVisible, value)) RefreshDraftActions(); }
    }
    public bool ShowAddDescription => HasDraftTitle && !IsDescriptionExpanded;
    public bool ShowAddSubTask => HasDraftTitle && !IsSubTaskInputVisible;
    public string DraftDescription
    {
        get => _draft?.Description ?? string.Empty;
        set
        {
            if (_draft is null) return;
            value ??= string.Empty;
            _draft.Description = value.Length > 200 ? value[..200] : value;
        }
    }
    public string DescriptionCountDisplay => $"{DraftDescription.Length}/200";
    public string SubTaskInput
    {
        get => _subTaskInput;
        set { if (Set(ref _subTaskInput, value)) _addSubTaskCommand.NotifyCanExecuteChanged(); }
    }
    public string SubTaskCountDisplay => $"{_draft?.SubTasks.Count ?? 0}/20";
    public bool CanEnterSubTask => _draft?.SubTasks.Count < 20;
    public bool HasPendingTasks => Tasks.Count > 0 || _exitingTasks.Count > 0;
    public bool HasTodayCompletedTasks => TodayCompletedTasks.Count > 0;
    public bool IsEmpty => !HasPendingTasks && !HasTodayCompletedTasks;
    public int TotalTaskCount => Tasks.Count + TodayCompletedTasks.Count;
    public string TaskProgress => TotalTaskCount == 0 ? string.Empty : $"{TodayCompletedTasks.Count}/{TotalTaskCount}";
    public string TaskProgressDisplay => TotalTaskCount == 0 ? string.Empty : $"{TaskProgress} 已推进";
    public double TaskProgressRatio => TotalTaskCount == 0 ? 0 : TodayCompletedTasks.Count / (double)TotalTaskCount;

    internal void RefreshTasks()
    {
        var current = (_session.ActiveTarget?.Tasks.AsEnumerable() ?? []).ToArray();
        var pending = current.Where(item => !item.IsCompleted).ToArray();
        var completed = _session.CompletedTasks.OrderBy(item => item.CompletedAtUtc).ToArray();
        foreach (var task in _exitingTasks.Where(task => !completed.Contains(task) || !_session.IsFocusing).ToArray())
        {
            _exitingTasks.Remove(task);
            CompletionAnimationCancelled?.Invoke(this, task);
        }
        // The persisted state and counts change immediately. Only the drawer's visual order waits.
        if (IsOpen && _session.IsFocusing && CompletionAnimationRequested is not null)
            foreach (var task in completed.Where(task => Tasks.Contains(task)).ToArray())
            {
                if (!_exitingTasks.Add(task)) continue;
                var request = new TaskCompletionPresentationEventArgs(task);
                CompletionAnimationRequested.Invoke(this, request);
                if (!request.Handled) _exitingTasks.Remove(task);
            }
        var visible = pending.Concat(completed.Where(task => !_exitingTasks.Contains(task))).ToList();
        var previous = VisibleTasks.ToArray();
        for (var index = 0; index < previous.Length; index++)
        {
            if (!_exitingTasks.Contains(previous[index])) continue;
            var preceding = previous.Take(index).Count(task => pending.Contains(task) || _exitingTasks.Contains(task));
            visible.Insert(Math.Min(preceding, visible.Count), previous[index]);
        }
        if (SelectedTask is not null && !visible.Contains(SelectedTask)) SelectTask(null);
        if (_editingTask is not null && !visible.Contains(_editingTask)) CancelCreation();
        SyncTasks(Tasks, pending);
        SyncTasks(TodayCompletedTasks, completed);
        SyncTasks(VisibleTasks, visible.ToArray());
        for (var index = 0; index < visible.Count; index++) visible[index].DrawerNumber = index + 1;
        var highlighted = _selectedTask is { IsCompleted: false } ? _selectedTask : pending.FirstOrDefault();
        foreach (var currentTask in current) currentTask.IsDrawerSelected = currentTask == highlighted;
        OnPropertyChanged(nameof(TaskProgress));
        OnPropertyChanged(nameof(TaskProgressDisplay));
        OnPropertyChanged(nameof(TaskProgressRatio));
        OnPropertyChanged(nameof(TotalTaskCount));
        OnPropertyChanged(nameof(HasPendingTasks));
        OnPropertyChanged(nameof(HasTodayCompletedTasks));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void FinishCompletionAnimation(FocusTaskViewModel task)
    {
        if (!_exitingTasks.Remove(task)) return;
        // Remove only the finished container: other simultaneous exits retain their animation clocks.
        VisibleTasks.Remove(task);
        RefreshTasks();
    }

    public void FinishCompletionAnimations()
    {
        foreach (var task in _exitingTasks.ToArray())
        {
            _exitingTasks.Remove(task);
            CompletionAnimationCancelled?.Invoke(this, task);
        }
        RefreshTasks();
    }

    public bool MoveTask(FocusTaskViewModel task, int destinationIndex)
    {
        if (!_session.IsFocusing || IsCreating || task.IsCompleted || _session.ActiveTarget is not { } target ||
            !Tasks.Contains(task) || destinationIndex < 0 || destinationIndex >= Tasks.Count)
            return false;
        var oldIndex = target.Tasks.IndexOf(task);
        var newIndex = target.Tasks.IndexOf(Tasks[destinationIndex]);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return false;
        // Move the existing model so its details and disclosure state travel together.
        // The session's collection subscription publishes the new persisted sort order.
        target.Tasks.Move(oldIndex, newIndex);
        return true;
    }

    private static void SyncTasks(ObservableCollection<FocusTaskViewModel> destination, FocusTaskViewModel[] desired)
    {
        foreach (var removed in destination.Where(item => !desired.Contains(item)).ToArray()) destination.Remove(removed);
        for (var index = 0; index < desired.Length; index++)
        {
            var item = desired[index];
            var oldIndex = destination.IndexOf(item);
            if (oldIndex < 0) destination.Insert(index, item);
            else if (oldIndex != index) destination.Move(oldIndex, index);
        }
    }

    internal void Close()
    {
        IsOpen = false;
        if (IsCreating) CancelCreation();
    }

    private void BeginCreation()
    {
        if (!_session.IsFocusing || _session.ActiveTarget is not { } target) return;
        SelectTask(null);
        CancelCreation();
        var now = DateTimeOffset.UtcNow;
        _draft = new FocusTaskViewModel(target.TargetId, string.Empty, createdAtUtc: now);
        _draft.SubTasks.CollectionChanged += DraftSubTasksChanged;
        _draft.PropertyChanged += DraftPropertyChanged;
        DraftTitle = string.Empty;
        SubTaskInput = string.Empty;
        IsCreating = true;
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(CommitButtonText));
        RefreshDraftCount();
    }

    private void BeginTaskEdit(FocusTaskViewModel? task)
    {
        if (task is null || !_session.IsFocusing || _session.ActiveTarget?.Tasks.Contains(task) != true) return;
        CancelCreation();
        _editingTask = task;
        _draft = new FocusTaskViewModel(task.TargetId, task.Name, taskId: task.TaskId, createdAtUtc: task.CreatedAtUtc)
        {
            Description = task.Description
        };
        foreach (var source in task.ExportSubTasks()) _draft.SubTasks.Add(new FocusSubTaskViewModel(source));
        _draft.SubTasks.CollectionChanged += DraftSubTasksChanged;
        _draft.PropertyChanged += DraftPropertyChanged;
        IsDescriptionExpanded = true;
        IsSubTasksExpanded = task.SubTasks.Count > 0;
        DraftTitle = task.Name;
        SubTaskInput = string.Empty;
        IsCreating = true;
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(IsEditingTask));
        OnPropertyChanged(nameof(CommitButtonText));
        RefreshDraftCount();
    }

    private void SelectTask(FocusTaskViewModel? task)
    {
        if (task is not null && (!_session.IsFocusing || _session.ActiveTarget?.Tasks.Contains(task) != true)) return;
        foreach (var currentTask in _session.ActiveTarget?.Tasks.AsEnumerable() ?? []) currentTask.IsDrawerSelected = false;
        if (_selectedTask is not null)
        {
            _selectedTask.IsExpanded = false;
            _selectedTask.IsDrawerSelected = false;
        }
        _selectedTask = task;
        if (task is not null)
        {
            CancelCreation();
            IsOpen = true;
            task.IsExpanded = true;
            task.IsDrawerSelected = true;
        }
        OnPropertyChanged(nameof(SelectedTask));
    }

    private void AddSubTask()
    {
        if (!_addSubTaskCommand.CanExecute(null) || _draft is null) return;
        var now = DateTimeOffset.UtcNow;
        _draft.SubTasks.Add(new FocusSubTaskViewModel(new LocalSubTaskDto(Guid.NewGuid().ToString("N"),
            _draft.TaskId, SubTaskInput.Trim(), false, _draft.SubTasks.Count, now, now)));
        SubTaskInput = string.Empty;
        IsSubTasksExpanded = true;
        IsSubTaskInputVisible = false;
    }

    private void CommitCreation()
    {
        if (!_createTaskCommand.CanExecute(null) || _draft is null || _session.ActiveTarget is not { } target) return;
        // Include a final unfinished input without discarding it when the main button is used.
        if (_addSubTaskCommand.CanExecute(null)) AddSubTask();
        var now = DateTimeOffset.UtcNow;
        var source = new LocalTaskDto(_draft.TaskId, target.TargetId, DraftTitle.Trim(),
            _editingTask?.IsCompleted ?? false, _editingTask is null ? target.Tasks.Count : target.Tasks.IndexOf(_editingTask), _draft.CreatedAtUtc, now)
        {
            Description = _draft.Description.Trim(),
            SubTasks = _draft.ExportSubTasks()
        };
        if (!_session.SaveDrawerTask(source)) return;
        CancelCreation();
    }

    private void CancelCreation()
    {
        if (_draft is not null)
        {
            _draft.SubTasks.CollectionChanged -= DraftSubTasksChanged;
            _draft.PropertyChanged -= DraftPropertyChanged;
        }
        _draft = null;
        _editingTask = null;
        IsDescriptionExpanded = false;
        IsSubTasksExpanded = false;
        IsSubTaskInputVisible = false;
        IsCreating = false;
        DraftTitle = string.Empty;
        SubTaskInput = string.Empty;
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(IsEditingTask));
        OnPropertyChanged(nameof(CommitButtonText));
        RefreshDraftCount();
    }

    private void RefreshDraftActions()
    {
        OnPropertyChanged(nameof(ShowAddDescription));
        OnPropertyChanged(nameof(ShowAddSubTask));
    }
    private void DraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusTaskViewModel.Description)) return;
        OnPropertyChanged(nameof(DraftDescription));
        OnPropertyChanged(nameof(DescriptionCountDisplay));
    }
    private void DraftSubTasksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshDraftCount();
    private void RefreshDraftCount()
    {
        OnPropertyChanged(nameof(DraftDescription));
        OnPropertyChanged(nameof(DescriptionCountDisplay));
        OnPropertyChanged(nameof(SubTaskCountDisplay));
        OnPropertyChanged(nameof(CanEnterSubTask));
        _addSubTaskCommand.NotifyCanExecuteChanged();
        _createTaskCommand.NotifyCanExecuteChanged();
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TaskCompletionPresentationEventArgs(FocusTaskViewModel task) : EventArgs
{
    public FocusTaskViewModel Task { get; } = task;
    public bool Handled { get; set; }
}
