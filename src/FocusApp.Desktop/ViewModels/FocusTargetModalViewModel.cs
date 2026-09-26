using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetModalViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<FocusTargetViewModel> _targets;
    private FocusTargetViewModel _selectedTarget;
    private bool _isOpen;
    private bool _hasSelectedTarget;

    public FocusTargetModalViewModel(bool useSampleData = true)
    {
        _targets = useSampleData ?
        [
            new FocusTargetViewModel("写代码", ["整理功能需求", "完成页面交互"]),
            new FocusTargetViewModel("学习", ["完成角色建模教程", "练习材质节点", "学习渲染基础", "学习 UV 展开"]),
            new FocusTargetViewModel("做设计", ["整理界面参考", "完成首页草图"]),
            new FocusTargetViewModel("阅读", ["阅读一章专业书"])
        ] : [];

        _selectedTarget = CreatePlaceholderTarget();
        OpenCommand = new RelayCommand<object>(_ => Open());
        CloseCommand = new RelayCommand<object>(_ => Close());
        CreateNewTargetCommand = new RelayCommand<object>(_ => RequestCreateTarget());
        SelectTargetCommand = new RelayCommand<FocusTargetViewModel>(SelectTarget);
        StartFocusCommand = new RelayCommand<object>(_ => RequestStartFocus(), _ => CanStartFocus);
        ManageTargetsCommand = new RelayCommand<object>(_ => RequestManageTargets());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CreateTargetRequested;
    public event EventHandler? ManageTargetsRequested;
    public event EventHandler? Opened;
    public event Action<FocusTargetViewModel>? StartFocusRequested;

    public IReadOnlyList<FocusTargetViewModel> Targets => _targets;

    public bool HasTargets => _targets.Count > 0;

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand CreateNewTargetCommand { get; }

    public ICommand SelectTargetCommand { get; }

    public ICommand StartFocusCommand { get; }

    public ICommand ManageTargetsCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool HasSelectedTarget
    {
        get => _hasSelectedTarget;
        private set => SetField(ref _hasSelectedTarget, value);
    }

    public bool CanStartFocus => HasSelectedTarget;

    public string? SelectedTargetId => HasSelectedTarget ? SelectedTarget.TargetId : null;

    public string SelectedTargetButtonText => HasSelectedTarget ? SelectedTarget.Name : "选择专注目标(可选)";

    public FocusTargetViewModel SelectedTarget
    {
        get => _selectedTarget;
        private set
        {
            if (ReferenceEquals(_selectedTarget, value)) return;
            _selectedTarget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTargetButtonText));
        }
    }

    public void ApplyState(
        IEnumerable<LocalTargetDto> targets,
        IEnumerable<LocalTaskDto> tasks,
        string? selectedTargetId)
    {
        var selectedId = selectedTargetId ?? (HasSelectedTarget ? SelectedTarget.TargetId : null);
        var existingTargets = _targets.ToDictionary(item => item.TargetId, StringComparer.Ordinal);
        var persistedTasks = tasks.ToList();

        _targets.Clear();
        foreach (var target in targets.Where(item => !item.IsArchived).OrderBy(item => item.SortOrder))
        {
            var viewModel = existingTargets.TryGetValue(target.TargetId, out var existing)
                ? existing
                : new FocusTargetViewModel(
                    target.Name,
                    targetId: target.TargetId,
                    iconFileName: target.IconFileName, iconColorHex: target.IconColorHex);
            viewModel.ApplyName(target.Name);
            viewModel.ApplyIcon(target.IconFileName, target.IconColorHex ?? TargetIconCatalog.DefaultColorHex);
            viewModel.ApplyArchived(false);

            ApplyTasks(viewModel, persistedTasks);

            _targets.Add(viewModel);
        }

        SetSelectedTarget(_targets.FirstOrDefault(item => item.TargetId == selectedId));
        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(HasTargets));
    }

    /// <summary>
    /// Updates only the nested task collections. Target objects, selection,
    /// and the target collection itself remain untouched.
    /// </summary>
    public void ApplyTasks(IEnumerable<LocalTaskDto> tasks)
    {
        var persistedTasks = tasks.ToList();
        foreach (var target in _targets)
        {
            ApplyTasks(target, persistedTasks);
        }
    }

    private static void ApplyTasks(
        FocusTargetViewModel target,
        IReadOnlyList<LocalTaskDto> persistedTasks)
    {
        var targetTasks = persistedTasks
            .Where(item => item.TargetId == target.TargetId)
            .OrderBy(item => item.SortOrder)
            .ToArray();
        var existingTaskMap = target.Tasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
        foreach (var removed in target.Tasks
                     .Where(item => targetTasks.All(task => task.TaskId != item.TaskId))
                     .ToArray())
        {
            target.RemoveTask(removed);
        }

        for (var index = 0; index < targetTasks.Length; index++)
        {
            var source = targetTasks[index];
            if (existingTaskMap.TryGetValue(source.TaskId, out var task))
            {
                task.ApplyName(source.Name);
                task.ApplyCreatedAt(source.CreatedAtUtc);
                task.ApplyCompletion(source.IsCompleted, source.CompletedAtUtc);
            }
            else
            {
                task = target.AddTask(
                    source.TaskId,
                    source.Name,
                    source.IsCompleted,
                    createdAtUtc: source.CreatedAtUtc,
                    completedAtUtc: source.CompletedAtUtc);
            }

            var oldIndex = target.Tasks.IndexOf(task);
            if (oldIndex != index)
            {
                target.Tasks.Move(oldIndex, index);
            }
        }
    }

    public void Open()
    {
        IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void Close() => IsOpen = false;

    private void SelectTarget(FocusTargetViewModel? target)
    {
        if (target is null || !_targets.Contains(target)) return;
        SetSelectedTarget(ReferenceEquals(SelectedTarget, target) && HasSelectedTarget ? null : target);
    }

    private void RequestStartFocus()
    {
        if (!CanStartFocus) return;
        var target = SelectedTarget;
        Close();
        StartFocusRequested?.Invoke(target);
    }

    private void RequestManageTargets()
    {
        Close();
        ManageTargetsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestCreateTarget()
    {
        Close();
        CreateTargetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetSelectedTarget(FocusTargetViewModel? target)
    {
        foreach (var item in _targets)
        {
            item.ApplySelection(ReferenceEquals(item, target));
        }

        HasSelectedTarget = target is not null;
        SelectedTarget = target ?? CreatePlaceholderTarget();
        OnPropertyChanged(nameof(CanStartFocus));
        OnPropertyChanged(nameof(SelectedTargetId));
        OnPropertyChanged(nameof(SelectedTargetButtonText));
        if (StartFocusCommand is RelayCommand<object> command)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private static FocusTargetViewModel CreatePlaceholderTarget() => new("未选择目标");

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
