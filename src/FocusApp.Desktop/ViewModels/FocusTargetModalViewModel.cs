using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetModalViewModel : INotifyPropertyChanged
{
    private const int VisibleTargetCount = 3;
    private readonly ObservableCollection<FocusTargetViewModel> _targets;
    private readonly ObservableCollection<FocusTaskViewModel> _emptyTasks = [];
    private readonly RelayCommand<object> _beginAddTaskCommand;
    private FocusTargetViewModel _selectedTarget;
    private bool _isOpen;
    private bool _isCreatingTarget;
    private bool _isAddingTask;
    private string _newTargetName = string.Empty;
    private string _newTaskName = string.Empty;
    private int _visibleTargetStart;
    private bool _hasSelectedTarget;
    private FocusTargetViewModel _draftSelectedTarget;
    private bool _hasDraftSelectedTarget;

    public FocusTargetModalViewModel(bool useSampleData = true)
    {
        _targets = useSampleData ?
        [
            new FocusTargetViewModel("写代码",
            [
                "整理功能需求",
                "完成页面交互"
            ]),
            new FocusTargetViewModel("学习",
            [
                "完成角色建模教程",
                "练习材质节点",
                "学习渲染基础",
                "学习 UV 展开"
            ]),
            new FocusTargetViewModel("做设计",
            [
                "整理界面参考",
                "完成首页草图"
            ]),
            new FocusTargetViewModel("阅读",
            [
                "阅读一章专业书"
            ])
        ] : [];

        _selectedTarget = _targets.FirstOrDefault() ?? new FocusTargetViewModel("未选择目标");
        _draftSelectedTarget = _selectedTarget;
        VisibleTargets = new ObservableCollection<FocusTargetViewModel>();
        RefreshVisibleTargets();

        OpenCommand = new RelayCommand<object>(_ => Open());
        CloseCommand = new RelayCommand<object>(_ => Close());
        CancelSelectionCommand = new RelayCommand<object>(_ => CancelSelection());
        ConfirmSelectionCommand = new RelayCommand<object>(_ => ConfirmSelection());
        SelectTargetCommand = new RelayCommand<FocusTargetViewModel>(SelectTarget);
        ShowPreviousTargetsCommand = new RelayCommand<object>(_ => ShowPreviousTargets());
        ShowNextTargetsCommand = new RelayCommand<object>(_ => ShowNextTargets());
        BeginCreateTargetCommand = new RelayCommand<object>(_ => BeginCreateTarget());
        CancelCreateTargetCommand = new RelayCommand<object>(_ => CancelCreateTarget());
        CreateTargetCommand = new RelayCommand<object>(_ => CreateTarget());
        _beginAddTaskCommand = new RelayCommand<object>(_ => BeginAddTask(), _ => HasActiveSelectedTarget);
        BeginAddTaskCommand = _beginAddTaskCommand;
        ConfirmAddTaskCommand = new RelayCommand<object>(_ => ConfirmAddTask());
        ToggleTaskMenuCommand = new RelayCommand<FocusTaskViewModel>(ToggleTaskMenu);
        BeginEditTaskCommand = new RelayCommand<FocusTaskViewModel>(BeginEditTask);
        ConfirmEditTaskCommand = new RelayCommand<FocusTaskViewModel>(ConfirmEditTask);
        DeleteTaskCommand = new RelayCommand<FocusTaskViewModel>(DeleteTask);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<FocusTargetViewModel>? TargetChanged;
    public event EventHandler<string?>? SelectionChanged;

    public ObservableCollection<FocusTargetViewModel> VisibleTargets { get; }

    public IReadOnlyList<FocusTargetViewModel> Targets => _targets;

    public void ApplyState(
        IEnumerable<LocalTargetDto> targets,
        IEnumerable<LocalTaskDto> tasks,
        string? selectedTargetId)
    {
        var selectedId = selectedTargetId ?? (HasSelectedTarget ? SelectedTarget.TargetId : null);
        var hadDraftSelection = IsOpen && HasDraftSelectedTarget;
        var draftSelectedId = hadDraftSelection ? DraftSelectedTarget.TargetId : selectedId;
        var existingTargets = _targets.ToDictionary(item => item.TargetId, StringComparer.Ordinal);
        _targets.Clear();
        foreach (var target in targets.Where(item => !item.IsArchived).OrderBy(item => item.SortOrder))
        {
            var viewModel = existingTargets.TryGetValue(target.TargetId, out var existing)
                ? existing
                : new FocusTargetViewModel(target.Name, targetId: target.TargetId);
            viewModel.ApplyName(target.Name);
            var persistedTasks = tasks.Where(item => item.TargetId == target.TargetId).OrderBy(item => item.SortOrder).ToList();
            var existingTaskMap = viewModel.Tasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
            foreach (var removed in viewModel.Tasks.Where(item => persistedTasks.All(task => task.TaskId != item.TaskId)).ToList())
                viewModel.RemoveTask(removed);
            foreach (var task in persistedTasks)
            {
                if (existingTaskMap.TryGetValue(task.TaskId, out var existingTask))
                {
                    existingTask.ApplyName(task.Name);
                    existingTask.IsCompleted = task.IsCompleted;
                }
                else viewModel.AddTask(task.TaskId, task.Name, task.IsCompleted);
            }
            _targets.Add(viewModel);
        }

        var selected = _targets.FirstOrDefault(item => item.TargetId == selectedId);
        var draftSelected = _targets.FirstOrDefault(item => item.TargetId == draftSelectedId);
        HasSelectedTarget = selected is not null;
        if (selected is not null) _selectedTarget = selected;
        _draftSelectedTarget = draftSelected ?? selected ?? _targets.FirstOrDefault() ?? _selectedTarget;
        _hasDraftSelectedTarget = IsOpen ? hadDraftSelection && draftSelected is not null : HasSelectedTarget;
        _visibleTargetStart = 0;
        RefreshVisibleTargets();
        RefreshTargetSelectionVisuals();
        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(SelectedTargetButtonText));
        OnPropertyChanged(nameof(CurrentTasks));
        OnPropertyChanged(nameof(HasMoreTargets));
    }

    public ObservableCollection<FocusTaskViewModel> CurrentTasks =>
        HasActiveSelectedTarget ? ActiveSelectedTarget.Tasks : _emptyTasks;

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand CancelSelectionCommand { get; }

    public ICommand ConfirmSelectionCommand { get; }

    public ICommand SelectTargetCommand { get; }

    public ICommand ShowPreviousTargetsCommand { get; }

    public ICommand ShowNextTargetsCommand { get; }

    public ICommand BeginCreateTargetCommand { get; }

    public ICommand CancelCreateTargetCommand { get; }

    public ICommand CreateTargetCommand { get; }

    public ICommand BeginAddTaskCommand { get; }

    public ICommand ConfirmAddTaskCommand { get; }

    public ICommand ToggleTaskMenuCommand { get; }

    public ICommand BeginEditTaskCommand { get; }

    public ICommand ConfirmEditTaskCommand { get; }

    public ICommand DeleteTaskCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool IsCreatingTarget
    {
        get => _isCreatingTarget;
        private set => SetField(ref _isCreatingTarget, value);
    }

    public bool IsAddingTask
    {
        get => _isAddingTask;
        private set => SetField(ref _isAddingTask, value);
    }

    public bool HasMoreTargets => _targets.Count > VisibleTargetCount;

    public bool CanShowPreviousTargets => _visibleTargetStart > 0;

    public bool CanShowNextTargets => _visibleTargetStart < Math.Max(0, _targets.Count - VisibleTargetCount);

    public bool HasSelectedTarget
    {
        get => _hasSelectedTarget;
        private set
        {
            if (!SetField(ref _hasSelectedTarget, value))
            {
                return;
            }

            _beginAddTaskCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CurrentTasks));
            if (!value)
            {
                IsAddingTask = false;
                NewTaskName = string.Empty;
            }
        }
    }

    public bool HasDraftSelectedTarget
    {
        get => _hasDraftSelectedTarget;
        private set
        {
            if (!SetField(ref _hasDraftSelectedTarget, value))
            {
                return;
            }

            _beginAddTaskCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CurrentTasks));
            if (!value)
            {
                IsAddingTask = false;
                NewTaskName = string.Empty;
            }
        }
    }

    public string SelectedTargetButtonText => HasSelectedTarget ? SelectedTarget.Name : "选择专注目标(可选)";

    public FocusTargetViewModel SelectedTarget
    {
        get => _selectedTarget;
        private set
        {
            if (ReferenceEquals(_selectedTarget, value))
            {
                return;
            }

            _selectedTarget.IsSelected = false;
            CloseTaskMenus();
            _selectedTarget = value;
            _selectedTarget.IsSelected = true;
            HasSelectedTarget = true;
            IsAddingTask = false;
            NewTaskName = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTasks));
            OnPropertyChanged(nameof(SelectedTargetButtonText));
        }
    }

    public FocusTargetViewModel DraftSelectedTarget
    {
        get => _draftSelectedTarget;
        private set
        {
            if (ReferenceEquals(_draftSelectedTarget, value))
            {
                return;
            }

            CloseTaskMenus();
            _draftSelectedTarget = value;
            HasDraftSelectedTarget = true;
            IsAddingTask = false;
            NewTaskName = string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTasks));
        }
    }

    private bool HasActiveSelectedTarget => IsOpen ? HasDraftSelectedTarget : HasSelectedTarget;

    private FocusTargetViewModel ActiveSelectedTarget => IsOpen ? DraftSelectedTarget : SelectedTarget;

    public string NewTargetName
    {
        get => _newTargetName;
        set
        {
            if (SetField(ref _newTargetName, value))
            {
                OnPropertyChanged(nameof(CanCreateTarget));
            }
        }
    }

    public bool CanCreateTarget => !string.IsNullOrWhiteSpace(NewTargetName);

    public string NewTaskName
    {
        get => _newTaskName;
        set => SetField(ref _newTaskName, value);
    }

    public void Open()
    {
        CancelTaskEdits();
        _draftSelectedTarget = SelectedTarget;
        _hasDraftSelectedTarget = HasSelectedTarget;
        IsCreatingTarget = false;
        IsAddingTask = false;
        NewTargetName = string.Empty;
        NewTaskName = string.Empty;
        CloseTaskMenus();
        IsOpen = true;
        RefreshTargetSelectionVisuals();
        _beginAddTaskCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DraftSelectedTarget));
        OnPropertyChanged(nameof(HasDraftSelectedTarget));
        OnPropertyChanged(nameof(CurrentTasks));
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        CancelTaskEdits();
        CommitDraftSelection();
        CloseModal();
    }

    private void CloseModal()
    {
        CloseTaskMenus();
        IsOpen = false;
        IsCreatingTarget = false;
        IsAddingTask = false;
    }

    private void CancelSelection()
    {
        if (IsOpen)
        {
            HasDraftSelectedTarget = false;
        }
        else
        {
            HasSelectedTarget = false;
        }

        ActiveSelectedTarget.IsSelected = false;
        CloseTaskMenus();
        IsCreatingTarget = false;
        IsAddingTask = false;
        if (!IsOpen)
        {
            OnPropertyChanged(nameof(SelectedTargetButtonText));
            SelectionChanged?.Invoke(this, null);
        }
    }

    private void ConfirmSelection()
    {
        if (HasDraftSelectedTarget)
        {
            CommitDraftSelection();
            CloseModal();
        }
    }

    private void CommitDraftSelection()
    {
        if (!ReferenceEquals(SelectedTarget, DraftSelectedTarget))
        {
            SelectedTarget = DraftSelectedTarget;
        }

        HasSelectedTarget = HasDraftSelectedTarget;
        SelectedTarget.IsSelected = HasSelectedTarget;
        OnPropertyChanged(nameof(SelectedTargetButtonText));
        SelectionChanged?.Invoke(this, HasSelectedTarget ? SelectedTarget.TargetId : null);
    }

    private void SelectTarget(FocusTargetViewModel? target)
    {
        if (target is not null)
        {
            IsCreatingTarget = false;
            NewTargetName = string.Empty;

            var selectedTarget = ActiveSelectedTarget;
            var hasSelectedTarget = HasActiveSelectedTarget;
            if (ReferenceEquals(selectedTarget, target))
            {
                if (IsOpen)
                {
                    HasDraftSelectedTarget = !hasSelectedTarget;
                    target.IsSelected = HasDraftSelectedTarget;
                }
                else
                {
                    HasSelectedTarget = !hasSelectedTarget;
                    target.IsSelected = HasSelectedTarget;
                    OnPropertyChanged(nameof(SelectedTargetButtonText));
                }

                CloseTaskMenus();
                return;
            }

            if (IsOpen)
            {
                DraftSelectedTarget = target;
                HasDraftSelectedTarget = true;
                RefreshTargetSelectionVisuals();
            }
            else
            {
                SelectedTarget = target;
            }
        }
    }

    private void ShowPreviousTargets()
    {
        if (!CanShowPreviousTargets) return;
        _visibleTargetStart--;
        RefreshVisibleTargets();
    }

    private void ShowNextTargets()
    {
        if (!CanShowNextTargets) return;
        _visibleTargetStart++;
        RefreshVisibleTargets();
    }

    private void BeginCreateTarget()
    {
        IsAddingTask = false;
        IsCreatingTarget = true;
        NewTargetName = string.Empty;
    }

    private void CancelCreateTarget()
    {
        IsCreatingTarget = false;
        NewTargetName = string.Empty;
    }

    private void CreateTarget()
    {
        var name = NewTargetName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var target = new FocusTargetViewModel(name);
        _targets.Insert(0, target);
        _visibleTargetStart = 0;
        RefreshVisibleTargets();
        if (IsOpen)
        {
            DraftSelectedTarget = target;
            RefreshTargetSelectionVisuals();
        }
        else
        {
            SelectedTarget = target;
        }
        IsCreatingTarget = false;
        NewTargetName = string.Empty;
        OnPropertyChanged(nameof(HasMoreTargets));
        OnPropertyChanged(nameof(CanShowPreviousTargets));
        OnPropertyChanged(nameof(CanShowNextTargets));
        TargetChanged?.Invoke(this, target);
    }

    private void BeginAddTask()
    {
        if (!HasActiveSelectedTarget)
        {
            return;
        }

        IsCreatingTarget = false;
        CloseTaskMenus();
        NewTaskName = string.Empty;
        IsAddingTask = true;
    }

    private void ConfirmAddTask()
    {
        if (!HasActiveSelectedTarget)
        {
            NewTaskName = string.Empty;
            IsAddingTask = false;
            return;
        }

        var name = NewTaskName.Trim();
        if (name.Length > 0)
        {
            ActiveSelectedTarget.AddTask(name, insertAtTop: true);
            TargetChanged?.Invoke(this, ActiveSelectedTarget);
        }

        NewTaskName = string.Empty;
        IsAddingTask = false;
    }

    private void ToggleTaskMenu(FocusTaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        if (!HasActiveSelectedTarget || task.TargetId != ActiveSelectedTarget.TargetId)
        {
            return;
        }

        foreach (var item in CurrentTasks)
        {
            item.IsMenuOpen = ReferenceEquals(item, task) && !item.IsMenuOpen;
        }
    }

    private void BeginEditTask(FocusTaskViewModel? task)
    {
        if (IsCurrentTask(task))
        {
            task!.BeginEdit();
        }
    }

    private void ConfirmEditTask(FocusTaskViewModel? task)
    {
        if (IsCurrentTask(task))
        {
            task!.CommitEdit();
            TargetChanged?.Invoke(this, ActiveSelectedTarget);
        }
    }

    private void DeleteTask(FocusTaskViewModel? task)
    {
        if (IsCurrentTask(task))
        {
            ActiveSelectedTarget.RemoveTask(task!);
            TargetChanged?.Invoke(this, ActiveSelectedTarget);
        }
    }

    private bool IsCurrentTask(FocusTaskViewModel? task) =>
        task is not null &&
        HasActiveSelectedTarget &&
        task.TargetId == ActiveSelectedTarget.TargetId &&
        CurrentTasks.Contains(task);

    private void RefreshTargetSelectionVisuals()
    {
        foreach (var target in _targets)
        {
            target.IsSelected = HasDraftSelectedTarget && ReferenceEquals(target, DraftSelectedTarget);
        }
    }

    private void RefreshVisibleTargets()
    {
        VisibleTargets.Clear();
        for (var index = 0; index < Math.Min(VisibleTargetCount, _targets.Count); index++)
        {
            VisibleTargets.Add(_targets[_visibleTargetStart + index]);
        }

        OnPropertyChanged(nameof(CanShowPreviousTargets));
        OnPropertyChanged(nameof(CanShowNextTargets));
    }

    private void CloseTaskMenus()
    {
        foreach (var target in _targets)
        {
            foreach (var task in target.Tasks)
            {
                task.IsMenuOpen = false;
            }
        }
    }

    private void CancelTaskEdits()
    {
        foreach (var target in _targets)
        {
            foreach (var task in target.Tasks.Where(task => task.IsEditing))
            {
                task.CancelEdit();
            }
        }
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
