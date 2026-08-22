using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetModalViewModel : INotifyPropertyChanged
{
    private const int VisibleTargetCount = 3;
    private readonly ObservableCollection<FocusTargetViewModel> _targets;
    private readonly RelayCommand<object> _beginAddTaskCommand;
    private FocusTargetViewModel _selectedTarget;
    private bool _isOpen;
    private bool _isCreatingTarget;
    private bool _isAddingTask;
    private string _newTargetName = string.Empty;
    private string _newTaskName = string.Empty;
    private int _visibleTargetStart;
    private bool _hasSelectedTarget;
    private FocusTargetViewModel? _selectedTargetBeforeOpen;
    private bool _hadSelectedTargetBeforeOpen;

    public FocusTargetModalViewModel()
    {
        _targets =
        [
            new FocusTargetViewModel("写代码",
            [
                new FocusTaskViewModel("整理功能需求"),
                new FocusTaskViewModel("完成页面交互")
            ]),
            new FocusTargetViewModel("学习",
            [
                new FocusTaskViewModel("完成角色建模教程"),
                new FocusTaskViewModel("练习材质节点"),
                new FocusTaskViewModel("学习渲染基础"),
                new FocusTaskViewModel("学习 UV 展开")
            ]),
            new FocusTargetViewModel("做设计",
            [
                new FocusTaskViewModel("整理界面参考"),
                new FocusTaskViewModel("完成首页草图")
            ]),
            new FocusTargetViewModel("阅读",
            [
                new FocusTaskViewModel("阅读一章专业书")
            ])
        ];

        _selectedTarget = _targets[1];
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
        _beginAddTaskCommand = new RelayCommand<object>(_ => BeginAddTask(), _ => HasSelectedTarget);
        BeginAddTaskCommand = _beginAddTaskCommand;
        ConfirmAddTaskCommand = new RelayCommand<object>(_ => ConfirmAddTask());
        ToggleTaskMenuCommand = new RelayCommand<FocusTaskViewModel>(ToggleTaskMenu);
        BeginEditTaskCommand = new RelayCommand<FocusTaskViewModel>(BeginEditTask);
        ConfirmEditTaskCommand = new RelayCommand<FocusTaskViewModel>(ConfirmEditTask);
        DeleteTaskCommand = new RelayCommand<FocusTaskViewModel>(DeleteTask);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FocusTargetViewModel> VisibleTargets { get; }

    public ObservableCollection<FocusTaskViewModel> CurrentTasks => SelectedTarget.Tasks;

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
        _selectedTargetBeforeOpen = SelectedTarget;
        _hadSelectedTargetBeforeOpen = HasSelectedTarget;
        IsCreatingTarget = false;
        IsAddingTask = false;
        NewTargetName = string.Empty;
        NewTaskName = string.Empty;
        CloseTaskMenus();
        IsOpen = true;
    }

    private void Close()
    {
        CancelTaskEdits();
        RestoreSelectionBeforeOpen();
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
        HasSelectedTarget = false;
        SelectedTarget.IsSelected = false;
        CloseTaskMenus();
        IsCreatingTarget = false;
        IsAddingTask = false;
        _selectedTargetBeforeOpen = SelectedTarget;
        _hadSelectedTargetBeforeOpen = false;
        OnPropertyChanged(nameof(SelectedTargetButtonText));
    }

    private void ConfirmSelection()
    {
        if (HasSelectedTarget)
        {
            CloseModal();
        }
    }

    private void RestoreSelectionBeforeOpen()
    {
        if (_selectedTargetBeforeOpen is not null && !ReferenceEquals(SelectedTarget, _selectedTargetBeforeOpen))
        {
            SelectedTarget = _selectedTargetBeforeOpen;
        }

        HasSelectedTarget = _hadSelectedTargetBeforeOpen;
        SelectedTarget.IsSelected = HasSelectedTarget;
        OnPropertyChanged(nameof(SelectedTargetButtonText));
    }

    private void SelectTarget(FocusTargetViewModel? target)
    {
        if (target is not null)
        {
            IsCreatingTarget = false;
            NewTargetName = string.Empty;

            if (ReferenceEquals(SelectedTarget, target))
            {
                HasSelectedTarget = !HasSelectedTarget;
                target.IsSelected = HasSelectedTarget;
                CloseTaskMenus();
                OnPropertyChanged(nameof(SelectedTargetButtonText));
                return;
            }

            SelectedTarget = target;
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
        SelectedTarget = target;
        IsCreatingTarget = false;
        NewTargetName = string.Empty;
        OnPropertyChanged(nameof(HasMoreTargets));
        OnPropertyChanged(nameof(CanShowPreviousTargets));
        OnPropertyChanged(nameof(CanShowNextTargets));
    }

    private void BeginAddTask()
    {
        if (!HasSelectedTarget)
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
        if (!HasSelectedTarget)
        {
            NewTaskName = string.Empty;
            IsAddingTask = false;
            return;
        }

        var name = NewTaskName.Trim();
        if (name.Length > 0)
        {
            SelectedTarget.Tasks.Insert(0, new FocusTaskViewModel(name));
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

        foreach (var item in SelectedTarget.Tasks)
        {
            item.IsMenuOpen = ReferenceEquals(item, task) && !item.IsMenuOpen;
        }
    }

    private static void BeginEditTask(FocusTaskViewModel? task)
    {
        task?.BeginEdit();
    }

    private static void ConfirmEditTask(FocusTaskViewModel? task)
    {
        task?.CommitEdit();
    }

    private void DeleteTask(FocusTaskViewModel? task)
    {
        if (task is not null)
        {
            SelectedTarget.Tasks.Remove(task);
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
