using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

/// <summary>
/// Presentation-only adapter for the focus floating window. The session remains
/// the single source of truth for time and task state.
/// </summary>
public sealed class FocusFloatingWindowViewModel : INotifyPropertyChanged, IDisposable
{
    public const double EmptyWindowHeight = 115;
    public const double TaskHeaderHeight = 46;
    public const double TaskRowHeight = 28;
    public const int VisibleTaskLimit = 3;
    public const double MaximumWindowHeight = EmptyWindowHeight + TaskHeaderHeight + TaskRowHeight * VisibleTaskLimit;
    private readonly FocusSessionViewModel _session;
    private readonly HashSet<FocusTaskViewModel> _subscribedTasks = [];
    private FocusTargetViewModel? _subscribedTarget;
    private bool _isDisposed;
    private readonly Dictionary<string, FocusTaskViewModel> _exitingTasks = [];
    private double? _presentationListHeight;
    private double _presentationHeaderHeight = TaskHeaderHeight;
    public const double TaskCreationHeight = 44;
    private bool _isCreatingTask;
    private bool _isSubmittingTask;
    private string _taskTitle = string.Empty;
    private string? _creationTargetId;
    private double _creationHeight;
    private double? _creationTaskAreaHeight;

    public FocusFloatingWindowViewModel(FocusSessionViewModel session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ToggleTaskCompletedCommand = new RelayCommand<FocusTaskViewModel>(task =>
        {
            if (!_isDisposed) _session.CompleteTaskAndSubTasks(task);
        });
        _session.PropertyChanged += Session_PropertyChanged;
        _session.PendingTasks.CollectionChanged += PendingTasks_CollectionChanged;
        SubscribeTasks();
        SubscribeTarget();
        RefreshPresentation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<TaskCompletionPresentationEventArgs>? CompletionAnimationRequested;
    public event EventHandler<FocusTaskViewModel>? CompletionAnimationCancelled;
    public event EventHandler? TaskCreationStateChanged;
    public event EventHandler<FocusTaskViewModel>? TaskCreated;
    public ObservableCollection<FocusTaskViewModel> VisibleTasks { get; } = [];

    public FocusSessionViewModel Session => _session;

    public string RemainingTimeDisplay => _session.RemainingTimeDisplay;

    public bool HasTarget => _session.HasTarget;

    public string TargetName => _session.TargetName;

    public double RemainingProgress => _session.RemainingProgress;

    public string TotalTimeDisplay => $"{_session.TotalFocusSeconds / 60:00}:{_session.TotalFocusSeconds % 60:00}";

    public IReadOnlyList<FocusTaskViewModel> PendingTasks =>
        (_session.ActiveTarget?.Tasks.AsEnumerable() ?? []).Where(task => !task.IsCompleted).ToArray();

    public int PendingTaskCount => PendingTasks.Count;

    public int TaskRowCount => PendingTasks.Sum(task => 1 + task.SubTasks.Count);

    public bool HasTaskOverflow => VisibleTasks.Sum(task => 1 + task.SubTasks.Count) * TaskRowHeight > AvailableTaskListHeight;

    private double AvailableTaskListHeight => TaskRowHeight * VisibleTaskLimit - CreationHeight;

    public double TaskListHeight => Math.Min(AvailableTaskListHeight, _presentationListHeight ??
        VisibleTasks.Sum(task => 1 + task.SubTasks.Count) * TaskRowHeight);

    public bool HasDisplayedTasks => VisibleTasks.Count > 0;

    public double WindowHeight => EmptyWindowHeight + TaskAreaHeight + CreationHeight;
    public double NaturalTaskAreaHeight => HasDisplayedTasks ? _presentationHeaderHeight + TaskListHeight : 0;
    internal double GetTaskAreaHeightForCreation(double creationHeight) => HasDisplayedTasks
        ? _presentationHeaderHeight + Math.Min(TaskRowHeight * VisibleTaskLimit - creationHeight,
            _presentationListHeight ?? VisibleTasks.Sum(task => 1 + task.SubTasks.Count) * TaskRowHeight) : 0;
    public double TaskAreaHeight => _creationTaskAreaHeight ?? NaturalTaskAreaHeight;
    public double CreationHeight => _creationHeight;
    public bool IsCreatingTask => _isCreatingTask;
    public string TaskTitle
    {
        get => _taskTitle;
        set
        {
            if (_taskTitle == value) return;
            _taskTitle = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool BeginTaskCreation()
    {
        if (_isDisposed || !IsFocusing || !HasTarget || IsCreatingTask || _isSubmittingTask) return false;
        _creationTargetId = _session.ActiveTarget!.TargetId;
        TaskTitle = string.Empty;
        _isCreatingTask = true;
        OnPropertyChanged(nameof(IsCreatingTask));
        RefreshCreationPresentation();
        return true;
    }

    public bool SubmitTaskCreation()
    {
        if (_isDisposed || _isSubmittingTask || !IsCreatingTask || !IsFocusing ||
            _session.ActiveTarget is not { } target || target.TargetId != _creationTargetId ||
            string.IsNullOrWhiteSpace(TaskTitle)) return false;
        _isSubmittingTask = true;
        // Keep the existing area while the shared save publishes its collection changes.
        _creationTaskAreaHeight = TaskAreaHeight;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var source = new LocalTaskDto(Guid.NewGuid().ToString("N"), target.TargetId,
                TaskTitle.Trim(), false, target.Tasks.Count, now, now);
            if (!_session.SaveDrawerTask(source)) { _creationTaskAreaHeight = null; return false; }
            CancelTaskCreation();
            TaskCreated?.Invoke(this, target.Tasks.Single(task => task.TaskId == source.TaskId));
            return true;
        }
        finally { _isSubmittingTask = false; }
    }

    public void CancelTaskCreation()
    {
        if (!IsCreatingTask) return;
        _isCreatingTask = false;
        _creationTargetId = null;
        TaskTitle = string.Empty;
        OnPropertyChanged(nameof(IsCreatingTask));
        RefreshCreationPresentation();
    }

    private void RefreshCreationPresentation()
    {
        if (TaskCreationStateChanged is { } handler) handler.Invoke(this, EventArgs.Empty);
        else SetCreationPresentation(IsCreatingTask ? TaskCreationHeight : 0);
    }

    public void SetCreationPresentation(double height, double? taskAreaHeight = null)
    {
        _creationHeight = Math.Clamp(height, 0, TaskCreationHeight);
        _creationTaskAreaHeight = taskAreaHeight;
        OnPropertyChanged(nameof(CreationHeight));
        OnPropertyChanged(nameof(TaskListHeight));
        OnPropertyChanged(nameof(HasTaskOverflow));
        OnPropertyChanged(nameof(TaskAreaHeight));
        OnPropertyChanged(nameof(WindowHeight));
    }

    public void SetCompletionPresentationHeight(double listHeight, double headerHeight)
    {
        listHeight = Math.Clamp(listHeight, 0, TaskRowHeight * VisibleTaskLimit);
        headerHeight = Math.Clamp(headerHeight, 0, TaskHeaderHeight);
        if (_presentationListHeight == listHeight && _presentationHeaderHeight == headerHeight) return;
        _presentationListHeight = listHeight;
        _presentationHeaderHeight = headerHeight;
        OnPropertyChanged(nameof(TaskListHeight));
        OnPropertyChanged(nameof(WindowHeight));
        OnPropertyChanged(nameof(TaskAreaHeight));
    }

    public void FinishCompletionAnimation(string taskId)
    {
        if (!_exitingTasks.Remove(taskId, out var task)) return;
        VisibleTasks.Remove(task);
        if (_exitingTasks.Count == 0) ResetPresentationHeight();
        NotifyPendingTasksChanged();
    }

    public void FinishCompletionAnimations()
    {
        foreach (var task in _exitingTasks.Values.ToArray())
        {
            _exitingTasks.Remove(task.TaskId);
            CompletionAnimationCancelled?.Invoke(this, task);
            VisibleTasks.Remove(task);
        }
        ResetPresentationHeight();
        NotifyPendingTasksChanged();
    }

    private void ResetPresentationHeight()
    {
        _presentationListHeight = null;
        _presentationHeaderHeight = TaskHeaderHeight;
    }

    private void RefreshPresentation()
    {
        var current = (_session.ActiveTarget?.Tasks.AsEnumerable() ?? []).ToArray();
        var previous = VisibleTasks.ToArray();
        foreach (var task in _exitingTasks.Values.Where(task => !task.IsCompleted || !current.Contains(task) || !IsFocusing).ToArray())
        {
            _exitingTasks.Remove(task.TaskId);
            CompletionAnimationCancelled?.Invoke(this, task);
        }
        if (!_isDisposed && IsFocusing && CompletionAnimationRequested is not null)
            foreach (var task in previous.Where(task => task.IsCompleted && current.Contains(task) && !_exitingTasks.ContainsKey(task.TaskId)))
            {
                _exitingTasks.Add(task.TaskId, task);
                var request = new TaskCompletionPresentationEventArgs(task);
                CompletionAnimationRequested.Invoke(this, request);
                if (!request.Handled) _exitingTasks.Remove(task.TaskId);
            }
        var desired = PendingTasks.ToList();
        for (var index = 0; index < previous.Length; index++)
        {
            if (!_exitingTasks.ContainsKey(previous[index].TaskId)) continue;
            var preceding = previous.Take(index).Count(task => desired.Contains(task));
            desired.Insert(Math.Min(preceding, desired.Count), previous[index]);
        }
        foreach (var removed in VisibleTasks.Where(task => !desired.Contains(task)).ToArray()) VisibleTasks.Remove(removed);
        for (var index = 0; index < desired.Count; index++)
        {
            var oldIndex = VisibleTasks.IndexOf(desired[index]);
            if (oldIndex < 0) VisibleTasks.Insert(index, desired[index]);
            else if (oldIndex != index) VisibleTasks.Move(oldIndex, index);
        }
        if (_exitingTasks.Count == 0) ResetPresentationHeight();
    }

    public string TaskProgressDisplay => $"{_session.SessionCompletedTaskCount} / {PendingTaskCount + _session.SessionCompletedTaskCount}";

    public bool IsFocusing => _session.IsFocusing;

    public FocusTaskViewModel? CurrentTask => PendingTasks.FirstOrDefault();

    public bool HasCurrentTask => CurrentTask is not null;

    public string CurrentTaskName => CurrentTask?.Name ?? string.Empty;

    public ICommand ToggleTaskCompletedCommand { get; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelTaskCreation();
        _isDisposed = true;
        FinishCompletionAnimations();
        _session.PropertyChanged -= Session_PropertyChanged;
        _session.PendingTasks.CollectionChanged -= PendingTasks_CollectionChanged;
        UnsubscribeTasks();
        if (_subscribedTarget is not null) _subscribedTarget.PropertyChanged -= Target_PropertyChanged;
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusSessionViewModel.ActiveTarget))
        {
            CancelTaskCreation();
            SubscribeTarget();
            OnPropertyChanged(nameof(HasTarget));
            OnPropertyChanged(nameof(TargetName));
            NotifyPendingTasksChanged();
        }
        if (e.PropertyName is nameof(FocusSessionViewModel.RemainingTimeDisplay)
            or nameof(FocusSessionViewModel.RemainingProgress)
            or nameof(FocusSessionViewModel.IsFocusing))
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(FocusSessionViewModel.IsFocusing))
            {
                if (!IsFocusing) CancelTaskCreation();
                NotifyPendingTasksChanged();
            }
        }
        if (e.PropertyName == nameof(FocusSessionViewModel.TotalFocusSeconds)) OnPropertyChanged(nameof(TotalTimeDisplay));
        if (e.PropertyName == nameof(FocusSessionViewModel.SessionCompletedTaskCount)) OnPropertyChanged(nameof(TaskProgressDisplay));
    }

    private void SubscribeTarget()
    {
        if (_subscribedTarget is not null) _subscribedTarget.PropertyChanged -= Target_PropertyChanged;
        _subscribedTarget = _session.ActiveTarget;
        if (_subscribedTarget is not null) _subscribedTarget.PropertyChanged += Target_PropertyChanged;
    }

    private void Target_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusTargetViewModel.Name)) OnPropertyChanged(nameof(TargetName));
    }

    private void PendingTasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeTasks();
        SubscribeTasks();
        NotifyPendingTasksChanged();
    }

    private void NotifyPendingTasksChanged()
    {
        RefreshPresentation();
        OnPropertyChanged(nameof(PendingTasks));
        OnPropertyChanged(nameof(PendingTaskCount));
        OnPropertyChanged(nameof(TaskRowCount));
        OnPropertyChanged(nameof(HasTaskOverflow));
        OnPropertyChanged(nameof(TaskListHeight));
        OnPropertyChanged(nameof(WindowHeight));
        OnPropertyChanged(nameof(TaskAreaHeight));
        OnPropertyChanged(nameof(TaskProgressDisplay));
        OnPropertyChanged(nameof(CurrentTask));
        OnPropertyChanged(nameof(HasCurrentTask));
        OnPropertyChanged(nameof(HasDisplayedTasks));
        OnPropertyChanged(nameof(CurrentTaskName));
    }

    private void SubscribeTasks()
    {
        foreach (var task in _session.PendingTasks)
        {
            if (_subscribedTasks.Add(task))
            {
                task.PropertyChanged += Task_PropertyChanged;
                task.SubTasks.CollectionChanged += SubTasks_CollectionChanged;
            }
        }
    }

    private void UnsubscribeTasks()
    {
        foreach (var task in _subscribedTasks)
        {
            task.PropertyChanged -= Task_PropertyChanged;
            task.SubTasks.CollectionChanged -= SubTasks_CollectionChanged;
        }

        _subscribedTasks.Clear();
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusTaskViewModel.Name))
        {
            OnPropertyChanged(nameof(CurrentTaskName));
        }
        if (e.PropertyName == nameof(FocusTaskViewModel.IsCompleted)) NotifyPendingTasksChanged();
    }

    private void SubTasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyPendingTasksChanged();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_isDisposed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
