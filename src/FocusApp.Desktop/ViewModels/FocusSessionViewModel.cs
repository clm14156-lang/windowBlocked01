using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusSessionViewModel : INotifyPropertyChanged
{
    private const int PreparationDurationSeconds = 5;
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _nowProvider;
    private readonly FocusSessionEngine _engine;
    private readonly Stopwatch _preparationStopwatch = new();
    private readonly Stopwatch _focusStopwatch = new();
    private FocusFlowStage _stage;
    private int _preparationSeconds = PreparationDurationSeconds;
    private double _preparationProgress;
    private TimeSpan _lastPreparationElapsed;
    private TimeSpan _lastFocusElapsed;
    private int _totalFocusSeconds;
    private int _remainingFocusSeconds;
    private int _completedFocusSeconds;
    private int _todayTotalSeconds;
    private DateTime _completedAt;
    private bool _isEndConfirmationOpen;
    private FocusTargetViewModel? _activeTarget;
    private bool _isCompletedTasksExpanded;
    private bool _isForcedModeActive;
    private readonly HashSet<FocusTaskViewModel> _sessionCompletedTaskSet = [];
    private readonly List<FocusSessionRecord> _completionHistory = [];
    private FocusSessionRecord? _lastRecordedCompletion;

    public FocusSessionViewModel(Func<DateTime>? nowProvider = null, bool runTimer = true)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
        _engine = new FocusSessionEngine(_nowProvider);
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => OnTimerTick();
        RunTimer = runTimer;

        CancelPreparationCommand = new RelayCommand<object>(_ => CancelPreparation());
        RequestEndCommand = new RelayCommand<object>(_ => OpenEndConfirmation());
        ContinueFocusCommand = new RelayCommand<object>(_ => ContinueFocus());
        ConfirmEndCommand = new RelayCommand<object>(_ => CompleteFocus());
        FocusAgainCommand = new RelayCommand<object>(_ => FocusAgain());
        ReturnHomeCommand = new RelayCommand<object>(_ => ReturnHome());
        AddTaskCommand = new RelayCommand<object>(_ => AddTask());
        ToggleTaskCompletedCommand = new RelayCommand<FocusTaskViewModel>(ToggleTaskCompleted);
        ToggleTaskMenuCommand = new RelayCommand<FocusTaskViewModel>(ToggleTaskMenu);
        BeginEditTaskCommand = new RelayCommand<FocusTaskViewModel>(BeginEditTask);
        ConfirmEditTaskCommand = new RelayCommand<FocusTaskViewModel>(ConfirmEditTask);
        DeleteTaskCommand = new RelayCommand<FocusTaskViewModel>(DeleteTask);
        ToggleCompletedTasksCommand = new RelayCommand<object>(_ => IsCompletedTasksExpanded = !IsCompletedTasksExpanded);
        DismissTaskMenusCommand = new RelayCommand<object>(_ => CloseTaskMenus());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CancelPreparationCommand { get; }

    public ICommand RequestEndCommand { get; }

    public ICommand ContinueFocusCommand { get; }

    public ICommand ConfirmEndCommand { get; }

    public ICommand FocusAgainCommand { get; }

    public ICommand ReturnHomeCommand { get; }

    public ICommand AddTaskCommand { get; }
    public ICommand ToggleTaskCompletedCommand { get; }
    public ICommand ToggleTaskMenuCommand { get; }
    public ICommand BeginEditTaskCommand { get; }
    public ICommand ConfirmEditTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand ToggleCompletedTasksCommand { get; }
    public ICommand DismissTaskMenusCommand { get; }

    public ObservableCollection<FocusTaskViewModel> PendingTasks { get; } = [];
    public ObservableCollection<FocusTaskViewModel> CompletedTasks { get; } = [];

    public ObservableCollection<FocusTaskViewModel> SessionCompletedTasks { get; } = [];

    public IReadOnlyList<FocusSessionRecord> CompletionHistory => _completionHistory;

    public FocusSessionRecord? LastCompletion => _completionHistory.Count == 0
        ? null
        : _completionHistory[^1];

    public int SessionCompletedTaskCount => SessionCompletedTasks.Count;

    public string SessionCompletedTaskSummary => $"本次完成 {SessionCompletedTaskCount} 个任务";

    public FocusTargetViewModel? ActiveTarget
    {
        get => _activeTarget;
        private set
        {
            if (ReferenceEquals(_activeTarget, value))
            {
                return;
            }

            UnsubscribeTarget(_activeTarget);
            _activeTarget = value;
            SubscribeTarget(_activeTarget);
            RefreshTaskGroups();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTarget));
            OnPropertyChanged(nameof(TargetName));
            OnPropertyChanged(nameof(PendingTaskCount));
            OnPropertyChanged(nameof(PendingTaskSummary));
        }
    }

    public bool HasTarget => ActiveTarget is not null;

    public string TargetName => ActiveTarget?.Name ?? string.Empty;

    public int PendingTaskCount => PendingTasks.Count;

    public string PendingTaskSummary => $"{PendingTaskCount} 个未完成任务";

    public bool IsCompletedTasksExpanded
    {
        get => _isCompletedTasksExpanded;
        private set
        {
            if (_isCompletedTasksExpanded == value)
            {
                return;
            }

            _isCompletedTasksExpanded = value;
            OnPropertyChanged();
        }
    }

    public FocusFlowStage Stage
    {
        get => _stage;
        private set
        {
            if (_stage == value)
            {
                return;
            }

            _stage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsFullScreenVisible));
            OnPropertyChanged(nameof(IsPreparing));
            OnPropertyChanged(nameof(IsFocusing));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsHomeConfigurationEnabled));
        }
    }

    public bool IsActive => Stage != FocusFlowStage.Idle;

    public bool IsForcedModeActive
    {
        get => _isForcedModeActive;
        private set
        {
            if (_isForcedModeActive == value) return;
            _isForcedModeActive = value;
            OnPropertyChanged();
        }
    }

    public bool IsFullScreenVisible => Stage is FocusFlowStage.Focusing or FocusFlowStage.Completed;

    public bool IsPreparing => Stage == FocusFlowStage.Preparing;

    public bool IsFocusing => Stage == FocusFlowStage.Focusing;

    public bool IsCompleted => Stage == FocusFlowStage.Completed;

    public bool IsHomeConfigurationEnabled => !IsPreparing;

    public bool IsEndConfirmationOpen
    {
        get => _isEndConfirmationOpen;
        private set
        {
            if (_isEndConfirmationOpen == value)
            {
                return;
            }

            _isEndConfirmationOpen = value;
            OnPropertyChanged();
        }
    }

    public int PreparationSeconds
    {
        get => _preparationSeconds;
        private set
        {
            if (_preparationSeconds == value)
            {
                return;
            }

            _preparationSeconds = value;
            OnPropertyChanged();
        }
    }

    public double PreparationProgress
    {
        get => _preparationProgress;
        private set
        {
            if (Math.Abs(_preparationProgress - value) < 0.0001)
            {
                return;
            }

            _preparationProgress = value;
            OnPropertyChanged();
        }
    }

    public string RemainingTimeDisplay => $"{RemainingFocusSeconds / 60:00}:{RemainingFocusSeconds % 60:00}";

    public double RemainingProgress => TotalFocusSeconds == 0 ? 0 : (double)RemainingFocusSeconds / TotalFocusSeconds;

    public string ElapsedTimeDisplay => FormatDuration(TotalFocusSeconds - RemainingFocusSeconds);

    public string CompletedDurationDisplay => FormatDuration(_completedFocusSeconds);

    public string CompletedDurationPrimaryValue => GetCompletedDurationParts().PrimaryValue;

    public string CompletedDurationPrimaryUnit => GetCompletedDurationParts().PrimaryUnit;

    public string CompletedDurationSecondaryValue => GetCompletedDurationParts().SecondaryValue;

    public string CompletedDurationSecondaryUnit => GetCompletedDurationParts().SecondaryUnit;

    public string TodayTotalDisplay => FormatDuration(_todayTotalSeconds);

    public string CompletedAtDisplay => _completedAt == default ? string.Empty : _completedAt.ToString("M月d日 HH:mm");

    public int TotalFocusSeconds => _totalFocusSeconds;

    public int RemainingFocusSeconds
    {
        get => _remainingFocusSeconds;
        private set
        {
            if (_remainingFocusSeconds == value)
            {
                return;
            }

            _remainingFocusSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RemainingTimeDisplay));
            OnPropertyChanged(nameof(RemainingProgress));
            OnPropertyChanged(nameof(ElapsedTimeDisplay));
        }
    }

    private bool RunTimer { get; }

    public void Start(int minutes, FocusTargetViewModel? target = null, bool forcedMode = false)
    {
        _timer.Stop();
        _preparationStopwatch.Reset();
        _focusStopwatch.Reset();
        _lastPreparationElapsed = TimeSpan.Zero;
        _lastFocusElapsed = TimeSpan.Zero;
        _engine.Start(minutes, forcedMode);
        _lastRecordedCompletion = null;
        SyncFromEngine();
        ActiveTarget = target;
        _sessionCompletedTaskSet.Clear();
        SessionCompletedTasks.Clear();
        OnPropertyChanged(nameof(SessionCompletedTaskCount));
        OnPropertyChanged(nameof(SessionCompletedTaskSummary));
        IsCompletedTasksExpanded = false;
        CloseTaskMenus();
        Stage = FocusFlowStage.Preparing;
        if (RunTimer)
        {
            _preparationStopwatch.Start();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Start();
        }
    }

    public void AdvanceOneSecond()
    {
        if (Stage == FocusFlowStage.Preparing)
        {
            AdvancePreparationBy(TimeSpan.FromSeconds(1));
            return;
        }

        if (Stage == FocusFlowStage.Focusing)
        {
            _engine.AdvanceFocusBy(TimeSpan.FromSeconds(1));
            SyncFromEngine();
        }
    }

    public void AdvancePreparationBy(TimeSpan elapsed)
    {
        if (Stage != FocusFlowStage.Preparing || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _engine.AdvancePreparationBy(elapsed);
        SyncFromEngine();
        if (_engine.State == FocusSessionState.Focusing)
        {
            _preparationStopwatch.Stop();
            _focusStopwatch.Start();
            _lastFocusElapsed = TimeSpan.Zero;
        }
    }

    public bool MovePendingTask(FocusTaskViewModel task, FocusTaskViewModel target, bool insertAfter)
    {
        if (Stage != FocusFlowStage.Focusing || ActiveTarget is null ||
            task.IsCompleted || target.IsCompleted || ReferenceEquals(task, target))
        {
            return false;
        }

        var tasks = ActiveTarget.Tasks;
        var oldIndex = tasks.IndexOf(task);
        var targetIndex = tasks.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0)
        {
            return false;
        }

        var newIndex = insertAfter
            ? oldIndex < targetIndex ? targetIndex : targetIndex + 1
            : oldIndex < targetIndex ? targetIndex - 1 : targetIndex;
        newIndex = Math.Clamp(newIndex, 0, tasks.Count - 1);
        if (newIndex == oldIndex)
        {
            return false;
        }

        tasks.Move(oldIndex, newIndex);
        return true;
    }

    private void OnTimerTick()
    {
        if (Stage == FocusFlowStage.Preparing)
        {
            var preparationElapsed = _preparationStopwatch.Elapsed - _lastPreparationElapsed;
            _lastPreparationElapsed = _preparationStopwatch.Elapsed;
            AdvancePreparationBy(preparationElapsed);
            return;
        }

        if (Stage != FocusFlowStage.Focusing || IsEndConfirmationOpen)
        {
            return;
        }

        var focusElapsed = _focusStopwatch.Elapsed - _lastFocusElapsed;
        _lastFocusElapsed = _focusStopwatch.Elapsed;
        _engine.AdvanceFocusBy(focusElapsed);
        SyncFromEngine();
    }

    private void CancelPreparation()
    {
        if (Stage != FocusFlowStage.Preparing)
        {
            return;
        }

        _timer.Stop();
        _preparationStopwatch.Reset();
        _focusStopwatch.Reset();
        _engine.CancelPreparation();
        SyncFromEngine();
    }

    private void OpenEndConfirmation()
    {
        if (_engine.RequestEnd())
        {
            IsEndConfirmationOpen = true;
            _timer.Stop();
        }
    }

    private void ContinueFocus()
    {
        if (!_engine.ContinueFocus())
        {
            return;
        }

        IsEndConfirmationOpen = false;
        StartTimerIfEnabled();
    }

    private void CompleteFocus()
    {
        if (!_engine.ConfirmEnd())
        {
            return;
        }

        _timer.Stop();
        _preparationStopwatch.Reset();
        _focusStopwatch.Stop();
        SyncFromEngine();
        OnPropertyChanged(nameof(CompletedDurationDisplay));
        OnPropertyChanged(nameof(CompletedDurationPrimaryValue));
        OnPropertyChanged(nameof(CompletedDurationPrimaryUnit));
        OnPropertyChanged(nameof(CompletedDurationSecondaryValue));
        OnPropertyChanged(nameof(CompletedDurationSecondaryUnit));
        OnPropertyChanged(nameof(TodayTotalDisplay));
        OnPropertyChanged(nameof(CompletedAtDisplay));
        Stage = FocusFlowStage.Completed;
    }

    private void ReturnHome()
    {
        _timer.Stop();
        _preparationStopwatch.Reset();
        _focusStopwatch.Reset();
        IsEndConfirmationOpen = false;
        IsForcedModeActive = false;
        _engine.ReturnHome();
        SyncFromEngine();
        Stage = FocusFlowStage.Idle;
    }

    private void FocusAgain()
    {
        if (Stage != FocusFlowStage.Completed)
        {
            return;
        }

        var configuredMinutes = _totalFocusSeconds / 60;
        Start(configuredMinutes, ActiveTarget, IsForcedModeActive);
        AdvancePreparationBy(FocusSessionEngine.PreparationDuration);
    }

    private void AddTask()
    {
        if (!HasTarget)
        {
            return;
        }

        // An Enter key can still reach the add button if the editor has not
        // acquired keyboard focus yet. Commit that edit instead of creating
        // a second task for the same interaction.
        var editingTask = ActiveTarget!.Tasks.FirstOrDefault(task => task.IsEditing);
        if (editingTask is not null)
        {
            ConfirmEditTask(editingTask);
            return;
        }

        CloseTaskMenus();
        var task = ActiveTarget!.AddTask("新任务", isNew: true);
        task.BeginEdit();
        RefreshTaskGroups();
    }

    private void ToggleTaskCompleted(FocusTaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        task.IsCompleted = !task.IsCompleted;
        CloseTaskMenus();
        RefreshTaskGroups();
    }

    private void ToggleTaskMenu(FocusTaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        foreach (var item in ActiveTarget?.Tasks ?? [])
        {
            item.IsFocusMenuOpen = ReferenceEquals(item, task) && !item.IsFocusMenuOpen;
        }
    }

    private static void BeginEditTask(FocusTaskViewModel? task)
    {
        task?.BeginEdit();
    }

    private void ConfirmEditTask(FocusTaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(task.EditName))
        {
            if (task.IsNew)
            {
                ActiveTarget?.RemoveTask(task);
            }
            else
            {
                task.CancelEdit();
            }

            RefreshTaskGroups();
            return;
        }

        task.CommitEdit();
        RefreshTaskGroups();
    }

    private void DeleteTask(FocusTaskViewModel? task)
    {
        if (task is not null)
        {
            ActiveTarget?.RemoveTask(task);
            _sessionCompletedTaskSet.Remove(task);
            SessionCompletedTasks.Remove(task);
            OnPropertyChanged(nameof(SessionCompletedTaskCount));
            OnPropertyChanged(nameof(SessionCompletedTaskSummary));
            RefreshTaskGroups();
        }
    }

    private void SubscribeTarget(FocusTargetViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        target.Tasks.CollectionChanged += Tasks_CollectionChanged;
        foreach (var task in target.Tasks)
        {
            task.PropertyChanged += Task_PropertyChanged;
        }
    }

    private void UnsubscribeTarget(FocusTargetViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        target.Tasks.CollectionChanged -= Tasks_CollectionChanged;
        foreach (var task in target.Tasks)
        {
            task.PropertyChanged -= Task_PropertyChanged;
        }
    }

    private void Tasks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (FocusTaskViewModel task in e.OldItems)
            {
                task.PropertyChanged -= Task_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (FocusTaskViewModel task in e.NewItems)
            {
                task.PropertyChanged += Task_PropertyChanged;
            }
        }

        RefreshTaskGroups();
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusTaskViewModel.IsCompleted) && sender is FocusTaskViewModel changedTask)
        {
            if (Stage == FocusFlowStage.Focusing && changedTask.IsCompleted)
            {
                if (_sessionCompletedTaskSet.Add(changedTask))
                {
                    SessionCompletedTasks.Add(changedTask);
                    OnPropertyChanged(nameof(SessionCompletedTaskCount));
                    OnPropertyChanged(nameof(SessionCompletedTaskSummary));
                }
            }
            else if (!changedTask.IsCompleted && _sessionCompletedTaskSet.Remove(changedTask))
            {
                SessionCompletedTasks.Remove(changedTask);
                OnPropertyChanged(nameof(SessionCompletedTaskCount));
                OnPropertyChanged(nameof(SessionCompletedTaskSummary));
            }
        }

        if (e.PropertyName is nameof(FocusTaskViewModel.IsCompleted) or nameof(FocusTaskViewModel.Name) or nameof(FocusTaskViewModel.IsEditing))
        {
            RefreshTaskGroups();
        }
    }

    private void RefreshTaskGroups()
    {
        PendingTasks.Clear();
        CompletedTasks.Clear();
        if (ActiveTarget is not null)
        {
            foreach (var task in ActiveTarget.Tasks)
            {
                (task.IsCompleted ? CompletedTasks : PendingTasks).Add(task);
            }
        }

        OnPropertyChanged(nameof(PendingTaskCount));
        OnPropertyChanged(nameof(PendingTaskSummary));
    }

    private void CloseTaskMenus()
    {
        foreach (var task in ActiveTarget?.Tasks ?? [])
        {
            task.IsMenuOpen = false;
            task.IsFocusMenuOpen = false;
        }
    }

    private void StartTimerIfEnabled()
    {
        if (RunTimer)
        {
            _focusStopwatch.Start();
            _lastFocusElapsed = _focusStopwatch.Elapsed;
            _timer.Start();
        }
    }

    private void SyncFromEngine()
    {
        _totalFocusSeconds = _engine.ConfiguredSeconds;
        OnPropertyChanged(nameof(TotalFocusSeconds));
        PreparationSeconds = _engine.PreparationSeconds;
        PreparationProgress = _engine.PreparationProgress;
        RemainingFocusSeconds = _engine.RemainingFocusSeconds;
        IsForcedModeActive = _engine.IsForcedMode;
        IsEndConfirmationOpen = _engine.IsEndConfirmationOpen;

        if (_engine.State == FocusSessionState.Completed &&
            _engine.Completion is not null &&
            !ReferenceEquals(_lastRecordedCompletion, _engine.Completion))
        {
            _lastRecordedCompletion = _engine.Completion;
            _completedFocusSeconds = _engine.ElapsedFocusSeconds;
            _todayTotalSeconds += _completedFocusSeconds;
            _completedAt = _engine.CompletedAt ?? _nowProvider();
            _completionHistory.Add(_engine.Completion);
            OnPropertyChanged(nameof(CompletedDurationDisplay));
            OnPropertyChanged(nameof(CompletedDurationPrimaryValue));
            OnPropertyChanged(nameof(CompletedDurationPrimaryUnit));
            OnPropertyChanged(nameof(CompletedDurationSecondaryValue));
            OnPropertyChanged(nameof(CompletedDurationSecondaryUnit));
            OnPropertyChanged(nameof(TodayTotalDisplay));
            OnPropertyChanged(nameof(CompletedAtDisplay));
            OnPropertyChanged(nameof(LastCompletion));
            OnPropertyChanged(nameof(CompletionHistory));
        }

        Stage = _engine.State switch
        {
            FocusSessionState.Preparing => FocusFlowStage.Preparing,
            FocusSessionState.Focusing => FocusFlowStage.Focusing,
            FocusSessionState.Completed => FocusFlowStage.Completed,
            _ => FocusFlowStage.Idle
        };
    }

    private static string FormatDuration(int totalSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分钟";
        }

        if (duration.TotalMinutes >= 1 && duration.Seconds == 0)
        {
            return $"{duration.Minutes} 分钟";
        }

        return $"{duration.Minutes} 分 {duration.Seconds:00} 秒";
    }

    private (string PrimaryValue, string PrimaryUnit, string SecondaryValue, string SecondaryUnit) GetCompletedDurationParts()
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, _completedFocusSeconds));
        if (duration.TotalHours >= 1)
        {
            return ($"{(int)duration.TotalHours}", "小时", $"{duration.Minutes}", "分钟");
        }

        if (duration.TotalMinutes >= 1 && duration.Seconds == 0)
        {
            return ($"{duration.Minutes}", "分钟", string.Empty, string.Empty);
        }

        return ($"{duration.Minutes}", "分", $"{duration.Seconds:00}", "秒");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
