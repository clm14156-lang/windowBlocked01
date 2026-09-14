using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;

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

        _selectedTarget = _targets.FirstOrDefault() ?? CreatePlaceholderTarget();
        OpenCommand = new RelayCommand<object>(_ => Open());
        CloseCommand = new RelayCommand<object>(_ => Close());
        CreateNewTargetCommand = new RelayCommand<object>(_ => RequestCreateTarget());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CreateTargetRequested;

    public IReadOnlyList<FocusTargetViewModel> Targets => _targets;

    public bool HasTargets => _targets.Count > 0;

    public ICommand OpenCommand { get; }

    public ICommand CloseCommand { get; }

    public ICommand CreateNewTargetCommand { get; }

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
                    iconFileName: target.IconFileName);
            viewModel.ApplyName(target.Name);
            viewModel.ApplyIcon(target.IconFileName);

            var targetTasks = persistedTasks
                .Where(item => item.TargetId == target.TargetId)
                .OrderBy(item => item.SortOrder)
                .ToList();
            var existingTaskMap = viewModel.Tasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
            foreach (var removed in viewModel.Tasks
                         .Where(item => targetTasks.All(task => task.TaskId != item.TaskId))
                         .ToList())
            {
                viewModel.RemoveTask(removed);
            }

            foreach (var task in targetTasks)
            {
                if (existingTaskMap.TryGetValue(task.TaskId, out var existingTask))
                {
                    existingTask.ApplyName(task.Name);
                    existingTask.ApplyCreatedAt(task.CreatedAtUtc);
                    existingTask.ApplyCompletion(task.IsCompleted, task.CompletedAtUtc);
                }
                else
                {
                    viewModel.AddTask(
                        task.TaskId,
                        task.Name,
                        task.IsCompleted,
                        createdAtUtc: task.CreatedAtUtc,
                        completedAtUtc: task.CompletedAtUtc);
                }
            }

            _targets.Add(viewModel);
        }

        var selected = _targets.FirstOrDefault(item => item.TargetId == selectedId);
        HasSelectedTarget = selected is not null;
        if (selected is not null)
        {
            SelectedTarget = selected;
        }
        else
        {
            _selectedTarget = _targets.FirstOrDefault() ?? CreatePlaceholderTarget();
            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(SelectedTargetButtonText));
        }

        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(SelectedTargetButtonText));
    }

    public void Open() => IsOpen = true;

    private void Close() => IsOpen = false;

    private void RequestCreateTarget()
    {
        Close();
        CreateTargetRequested?.Invoke(this, EventArgs.Empty);
    }

    private static FocusTargetViewModel CreatePlaceholderTarget() =>
        new("未选择目标");

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
