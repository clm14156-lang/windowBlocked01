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
    private string _draftTitle = string.Empty;
    private string _subTaskInput = string.Empty;
    private FocusTaskViewModel? _draft;
    private FocusTaskViewModel? _editingTask;
    private FocusTaskViewModel? _selectedTask;

    public FocusTaskDrawerViewModel(FocusSessionViewModel session)
    {
        _session = session;
        ToggleCommand = new RelayCommand<object>(_ => { if (_session.IsFocusing && _session.HasTarget) IsOpen = !IsOpen; });
        CloseCommand = new RelayCommand<object>(_ => Close());
        AddTaskCommand = new RelayCommand<object>(_ => BeginCreation());
        CancelCreationCommand = new RelayCommand<object>(_ => CancelCreation());
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
    public ObservableCollection<FocusTaskViewModel> Tasks { get; } = [];
    public ICommand ToggleCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand AddTaskCommand { get; }
    public ICommand CancelCreationCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand EditTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand CreateTaskCommand { get; }
    public ICommand AddSubTaskCommand { get; }
    public ICommand DeleteSubTaskCommand { get; }
    public FocusTaskViewModel? SelectedTask => _selectedTask;
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (!Set(ref _isOpen, value)) return;
            if (!value) { CancelCreation(); SelectTask(null); }
            _session.NotifyTaskDrawerChanged();
        }
    }
    public bool IsCreating { get => _isCreating; private set => Set(ref _isCreating, value); }
    public bool IsEditingTask => _editingTask is not null;
    public string CommitButtonText => IsEditingTask ? "保存修改" : "创建任务";
    public string DraftTitle
    {
        get => _draftTitle;
        set { if (Set(ref _draftTitle, value)) _createTaskCommand.NotifyCanExecuteChanged(); }
    }
    public FocusTaskViewModel? Draft => _draft;
    public string SubTaskInput
    {
        get => _subTaskInput;
        set { if (Set(ref _subTaskInput, value)) _addSubTaskCommand.NotifyCanExecuteChanged(); }
    }
    public string SubTaskCountDisplay => $"{_draft?.SubTasks.Count ?? 0}/20";
    public bool CanEnterSubTask => _draft?.SubTasks.Count < 20;
    public string TaskProgress => $"{Tasks.Count(task => task.IsCompleted)}/{Tasks.Count}";

    internal void RefreshTasks()
    {
        var current = _session.CurrentRoundTasks.ToArray();
        var pending = current.Where(item => !item.IsCompleted).ToArray();
        var completed = current.Where(item => item.IsCompleted).ToArray();
        if (SelectedTask is not null && !current.Contains(SelectedTask)) SelectTask(null);
        if (_editingTask is not null && !current.Contains(_editingTask)) CancelCreation();
        SyncTasks(Tasks, pending.Concat(completed).ToArray());
        for (var index = 0; index < pending.Length; index++) pending[index].DrawerNumber = index + 1;
        var highlighted = _selectedTask is { IsCompleted: false } ? _selectedTask : pending.FirstOrDefault();
        foreach (var currentTask in current) currentTask.IsDrawerSelected = currentTask == highlighted;
        OnPropertyChanged(nameof(TaskProgress));
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
        foreach (var currentTask in _session.CurrentRoundTasks) currentTask.IsDrawerSelected = false;
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
        if (_draft is not null) _draft.SubTasks.CollectionChanged -= DraftSubTasksChanged;
        _draft = null;
        _editingTask = null;
        IsCreating = false;
        DraftTitle = string.Empty;
        SubTaskInput = string.Empty;
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(IsEditingTask));
        OnPropertyChanged(nameof(CommitButtonText));
        RefreshDraftCount();
    }

    private void DraftSubTasksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshDraftCount();
    private void RefreshDraftCount()
    {
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
