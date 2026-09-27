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
    private readonly RelayCommand<object> _saveEditCommand;
    private bool _isOpen;
    private bool _isCreating;
    private bool _isDetailsExpanded;
    private string _draftTitle = string.Empty;
    private string _subTaskInput = string.Empty;
    private FocusTaskViewModel? _draft;
    private FocusTaskViewModel? _selectedTask;
    private FocusSubTaskViewModel? _editingSubTask;
    private string? _editField;
    private string _editValue = string.Empty;

    public FocusTaskDrawerViewModel(FocusSessionViewModel session)
    {
        _session = session;
        ToggleCommand = new RelayCommand<object>(_ => { if (_session.IsFocusing && _session.HasTarget) IsOpen = !IsOpen; });
        CloseCommand = new RelayCommand<object>(_ => Close());
        AddTaskCommand = new RelayCommand<object>(_ => BeginCreation());
        CancelCreationCommand = new RelayCommand<object>(_ => CancelCreation());
        ToggleDetailsCommand = new RelayCommand<object>(_ => IsDetailsExpanded = !IsDetailsExpanded);
        ToggleExpandedCommand = new RelayCommand<FocusTaskViewModel>(task => SelectTask(task == SelectedTask ? null : task));
        EditTaskCommand = new RelayCommand<FocusTaskViewModel>(task => BeginEdit(task, "任务名称"));
        EditRemarkCommand = new RelayCommand<FocusTaskViewModel>(task => BeginEdit(task, "备注"));
        EditSubTaskCommand = new RelayCommand<FocusSubTaskViewModel>(item => BeginEdit(SelectedTask, "子任务名称", item));
        DeleteDetailSubTaskCommand = new RelayCommand<FocusSubTaskViewModel>(item =>
        {
            if (item is null) return;
            if (item == _editingSubTask) CancelEdit();
            SelectedTask?.SubTasks.Remove(item);
        });
        _saveEditCommand = new RelayCommand<object>(_ => CommitEdit(), _ => IsEditingDetails && (IsMultilineEdit || !string.IsNullOrWhiteSpace(EditValue)));
        SaveEditCommand = _saveEditCommand;
        CancelEditCommand = new RelayCommand<object>(_ => CancelEdit());
        DeleteTaskCommand = new RelayCommand<FocusTaskViewModel>(task => _session.DeleteTaskCommand.Execute(task));
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
    public ICommand ToggleDetailsCommand { get; }
    public ICommand ToggleExpandedCommand { get; }
    public ICommand EditTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand CreateTaskCommand { get; }
    public ICommand AddSubTaskCommand { get; }
    public ICommand DeleteSubTaskCommand { get; }
    public ICommand EditRemarkCommand { get; }
    public ICommand EditSubTaskCommand { get; }
    public ICommand DeleteDetailSubTaskCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public FocusTaskViewModel? SelectedTask => _selectedTask;
    public bool IsEditingDetails => _editField is not null;
    public bool IsMultilineEdit => _editField == "备注";
    public string EditLabel => $"编辑{_editField}";
    public string EditValue
    {
        get => _editValue;
        set { if (Set(ref _editValue, value)) _saveEditCommand.NotifyCanExecuteChanged(); }
    }
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
    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        private set { if (Set(ref _isDetailsExpanded, value)) OnPropertyChanged(nameof(DetailsToggleText)); }
    }
    public string DetailsToggleText => IsDetailsExpanded ? "收起详情" : "展开详情";
    public string CommitButtonText => "创建任务";
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
    public string TaskProgress => $"{Tasks.Count(item => item.IsCompleted)}/{Tasks.Count}";

    internal void RefreshTasks()
    {
        var desired = _session.ActiveTarget?.Tasks.OrderBy(item => item.IsCompleted).ToArray() ?? [];
        if (SelectedTask is not null && !desired.Contains(SelectedTask)) SelectTask(null);
        foreach (var removed in Tasks.Where(item => !desired.Contains(item)).ToArray()) Tasks.Remove(removed);
        for (var index = 0; index < desired.Length; index++)
        {
            var item = desired[index];
            item.DrawerNumber = index + 1;
            var oldIndex = Tasks.IndexOf(item);
            if (oldIndex < 0) Tasks.Insert(index, item);
            else if (oldIndex != index) Tasks.Move(oldIndex, index);
        }
        OnPropertyChanged(nameof(TaskProgress));
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
        IsDetailsExpanded = false;
        IsCreating = true;
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(CommitButtonText));
        RefreshDraftCount();
    }

    private void SelectTask(FocusTaskViewModel? task)
    {
        if (task is not null && (!_session.IsFocusing || _session.ActiveTarget?.Tasks.Contains(task) != true)) return;
        CancelEdit();
        if (_selectedTask is not null) _selectedTask.IsExpanded = false;
        _selectedTask = task;
        if (task is not null)
        {
            CancelCreation();
            IsOpen = true;
            task.IsExpanded = true;
        }
        OnPropertyChanged(nameof(SelectedTask));
    }

    private void BeginEdit(FocusTaskViewModel? task, string field, FocusSubTaskViewModel? subTask = null)
    {
        if (task is null || _session.ActiveTarget?.Tasks.Contains(task) != true) return;
        if (field == "子任务名称" && (subTask is null || !task.SubTasks.Contains(subTask))) return;
        SelectTask(task);
        _editingSubTask = subTask;
        _editField = field;
        EditValue = field switch { "备注" => task.Description, "子任务名称" => subTask!.Title, _ => task.Name };
        NotifyEditState();
    }

    private void CommitEdit()
    {
        if (SelectedTask is not { } task || _editField is null) return;
        var value = EditValue.Trim();
        if (_editField != "备注" && value.Length == 0) return;
        if (_editField == "子任务名称" && (_editingSubTask is null || !task.SubTasks.Contains(_editingSubTask)))
        { CancelEdit(); return; }
        if (_editField == "备注") task.Description = value;
        else if (_editField == "子任务名称" && _editingSubTask is not null) _editingSubTask.Title = value;
        else task.ApplyName(value);
        CancelEdit();
    }

    private void CancelEdit()
    {
        _editField = null;
        _editingSubTask = null;
        EditValue = string.Empty;
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(IsEditingDetails));
        OnPropertyChanged(nameof(IsMultilineEdit));
        OnPropertyChanged(nameof(EditLabel));
        _saveEditCommand.NotifyCanExecuteChanged();
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
            false, target.Tasks.Count, _draft.CreatedAtUtc, now)
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
        IsCreating = false;
        DraftTitle = string.Empty;
        SubTaskInput = string.Empty;
        OnPropertyChanged(nameof(Draft));
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
