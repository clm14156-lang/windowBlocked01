using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetModalViewModel : INotifyPropertyChanged
{
    private const int VisibleTargetCount = 3;
    private readonly ObservableCollection<FocusTargetViewModel> _targets;
    private FocusTargetViewModel _selectedTarget;
    private bool _isOpen;
    private bool _isCreatingTarget;
    private bool _isAddingTask;
    private string _newTargetName = string.Empty;
    private string _newTaskName = string.Empty;
    private int _visibleTargetStart;
    private bool _hasSelectedTarget;

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
        SelectTargetCommand = new RelayCommand<FocusTargetViewModel>(SelectTarget);
        ShowMoreTargetsCommand = new RelayCommand<object>(_ => ShowMoreTargets());
        BeginCreateTargetCommand = new RelayCommand<object>(_ => BeginCreateTarget());
        CancelCreateTargetCommand = new RelayCommand<object>(_ => CancelCreateTarget());
        CreateTargetCommand = new RelayCommand<object>(_ => CreateTarget());
        BeginAddTaskCommand = new RelayCommand<object>(_ => BeginAddTask());
        ConfirmAddTaskCommand = new RelayCommand<object>(_ => ConfirmAddTask());
        ToggleTaskMenuCommand = new RelayCommand<FocusTaskViewModel>(ToggleTaskMenu);
        BeginEditTaskCommand = new RelayCommand<FocusTaskViewModel>(BeginEditTask);
        ConfirmEditTaskCommand = new RelayCommand<FocusTaskViewModel>(ConfirmEditTask);
        DeleteTaskCommand = new RelayCommand<FocusTaskViewModel>(DeleteTask);
        ClearTargetCommand = new RelayCommand<object>(_ => ClearTarget());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FocusTargetViewModel> VisibleTargets { get; }

    public ObservableCollection<FocusTaskViewModel> CurrentTasks => SelectedTarget.Tasks;

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand SelectTargetCommand { get; }

    public ICommand ShowMoreTargetsCommand { get; }

    public ICommand BeginCreateTargetCommand { get; }

    public ICommand CancelCreateTargetCommand { get; }

    public ICommand CreateTargetCommand { get; }

    public ICommand BeginAddTaskCommand { get; }

    public ICommand ConfirmAddTaskCommand { get; }

    public ICommand ToggleTaskMenuCommand { get; }

    public ICommand BeginEditTaskCommand { get; }

    public ICommand ConfirmEditTaskCommand { get; }

    public ICommand DeleteTaskCommand { get; }

    public ICommand ClearTargetCommand { get; }

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

    public bool HasSelectedTarget
    {
        get => _hasSelectedTarget;
        private set => SetField(ref _hasSelectedTarget, value);
    }

    public string SelectedTargetButtonText => HasSelectedTarget ? $"本次专注目标：{SelectedTarget.Name}" : "本次专注目标（可选）";

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
        IsCreatingTarget = false;
        IsAddingTask = false;
        NewTargetName = string.Empty;
        NewTaskName = string.Empty;
        CloseTaskMenus();
        IsOpen = true;
    }

    private void Close()
    {
        CloseTaskMenus();
        IsOpen = false;
        IsCreatingTarget = false;
        IsAddingTask = false;
    }

    private void SelectTarget(FocusTargetViewModel? target)
    {
        if (target is not null)
        {
            if (ReferenceEquals(SelectedTarget, target))
            {
                HasSelectedTarget = true;
                OnPropertyChanged(nameof(SelectedTargetButtonText));
                return;
            }

            SelectedTarget = target;
        }
    }

    private void ClearTarget()
    {
        HasSelectedTarget = false;
        SelectedTarget.IsSelected = false;
        CloseTaskMenus();
        OnPropertyChanged(nameof(SelectedTargetButtonText));
    }

    private void ShowMoreTargets()
    {
        if (!HasMoreTargets)
        {
            return;
        }

        _visibleTargetStart = (_visibleTargetStart + VisibleTargetCount) % _targets.Count;
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
    }

    private void BeginAddTask()
    {
        IsCreatingTarget = false;
        CloseTaskMenus();
        NewTaskName = string.Empty;
        IsAddingTask = true;
    }

    private void ConfirmAddTask()
    {
        var name = NewTaskName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        SelectedTarget.Tasks.Insert(0, new FocusTaskViewModel(name));
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
            VisibleTargets.Add(_targets[(_visibleTargetStart + index) % _targets.Count]);
        }
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
