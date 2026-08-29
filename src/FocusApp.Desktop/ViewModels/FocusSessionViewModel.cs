using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Contracts;
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
    private FocusTargetViewModel? _sessionTarget;
    private Func<bool> _canStartForcedMode = static () => true;
    private Guid? _authoritativeSessionId;
    private LocalFocusSessionDto? _authoritativeSession;
    private Guid? _lastAuthoritativeCompletionSessionId;
    private bool _applyingAuthoritativeSession;
    private bool _isExternalStarting;
    private Action? _cancelExternalStarting;

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

    public event EventHandler<FocusSessionCompletedEventArgs>? CompletionRecorded;

    public event EventHandler? AuthoritativeTasksChanged;

    public event EventHandler<FocusTargetViewModel>? TargetTasksChanged;

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
            OnPropertyChanged(nameof(ActiveTargetId));
            OnPropertyChanged(nameof(PendingTaskCount));
            OnPropertyChanged(nameof(PendingTaskSummary));
        }
    }

    public bool HasTarget => ActiveTarget is not null;

    public string TargetName => ActiveTarget?.Name ?? string.Empty;

    public string? ActiveTargetId => ActiveTarget?.TargetId;

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

    public bool IsServiceOwnedForcedSession => _authoritativeSessionId is not null;

    public Guid? AuthoritativeSessionId => _authoritativeSessionId;

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

    public void SetForcedModeStartPermission(Func<bool> canStartForcedMode)
    {
        _canStartForcedMode = canStartForcedMode ?? throw new ArgumentNullException(nameof(canStartForcedMode));
    }

    public void BeginForcedFocusStarting(int minutes, FocusTargetViewModel? target, Action cancel)
    {
        if (Stage is FocusFlowStage.Preparing or FocusFlowStage.Focusing || minutes <= 0)
        {
            return;
        }

        _timer.Stop();
        _authoritativeSessionId = null;
        _authoritativeSession = null;
        _totalFocusSeconds = checked(minutes * 60);
        _remainingFocusSeconds = _totalFocusSeconds;
        _preparationSeconds = PreparationDurationSeconds;
        _preparationProgress = 0;
        _sessionTarget = target;
        ActiveTarget = target;
        IsForcedModeActive = true;
        _isExternalStarting = true;
        _cancelExternalStarting = cancel;
        IsEndConfirmationOpen = false;
        Stage = FocusFlowStage.Preparing;
        OnPropertyChanged(nameof(PreparationSeconds));
        OnPropertyChanged(nameof(PreparationProgress));
        OnPropertyChanged(nameof(RemainingFocusSeconds));
    }

    public void ClearForcedFocusStarting()
    {
        _isExternalStarting = false;
        _cancelExternalStarting = null;
        ResetToIdleCore();
    }

    public bool Start(int minutes, FocusTargetViewModel? target = null, bool forcedMode = false)
    {
        if (Stage is FocusFlowStage.Preparing or FocusFlowStage.Focusing)
        {
            return false;
        }

        if (forcedMode && !_canStartForcedMode())
        {
            return false;
        }

        _timer.Stop();
        _isExternalStarting = false;
        _cancelExternalStarting = null;
        _preparationStopwatch.Reset();
        _focusStopwatch.Reset();
        _lastPreparationElapsed = TimeSpan.Zero;
        _lastFocusElapsed = TimeSpan.Zero;
        _sessionTarget = target;
        _engine.Start(
            minutes,
            forcedMode,
            target is null ? null : new FocusSessionTargetContext(target.TargetId, target.Name));
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

        return true;
    }

    public void ApplyAuthoritativeSession(
        LocalFocusSessionDto session,
        FocusTargetViewModel? target = null,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsForcedMode)
        {
            throw new ArgumentException("只能将后台强制会话投影到强制模式界面。", nameof(session));
        }

        var isSameSession = _authoritativeSessionId == session.SessionId;
        if (!isSameSession)
        {
            _timer.Stop();
            _preparationStopwatch.Reset();
            _focusStopwatch.Reset();
        }
        if (session.Status != LocalFocusSessionStatusDto.Preparing)
        {
            _isExternalStarting = false;
            _cancelExternalStarting = null;
        }
        _authoritativeSessionId = session.SessionId;
        _authoritativeSession = session;
        OnPropertyChanged(nameof(IsServiceOwnedForcedSession));
        OnPropertyChanged(nameof(AuthoritativeSessionId));
        _totalFocusSeconds = session.ConfiguredSeconds;
        OnPropertyChanged(nameof(TotalFocusSeconds));
        IsForcedModeActive = true;
        IsEndConfirmationOpen = false;

        var projectedTarget = target;
        if (projectedTarget is null && !string.IsNullOrWhiteSpace(session.TargetNameSnapshot))
        {
            projectedTarget = new FocusTargetViewModel(
                session.TargetNameSnapshot,
                session.CompletedTasks.OrderBy(task => task.SortOrder).Select(task => task.TaskNameSnapshot));
            foreach (var task in projectedTarget.Tasks)
            {
                task.IsCompleted = true;
            }
        }

        _applyingAuthoritativeSession = true;
        try
        {
            _sessionTarget = projectedTarget;
            ActiveTarget = projectedTarget;
            SynchronizeAuthoritativeCompletedTasks(session);
        }
        finally
        {
            _applyingAuthoritativeSession = false;
        }
        RefreshAuthoritativeTime(nowUtc ?? DateTimeOffset.UtcNow);

        if (session.Status == LocalFocusSessionStatusDto.Completed)
        {
            RecordAuthoritativeCompletion(session);
            _timer.Stop();
        }
        else if (RunTimer)
        {
            // Preparation progress is a visual animation and needs a render-rate
            // cadence. The authoritative clock still comes from the service.
            _timer.Interval = session.Status == LocalFocusSessionStatusDto.Preparing
                ? TimeSpan.FromMilliseconds(16)
                : TimeSpan.FromMilliseconds(250);
            _timer.Start();
        }
    }

    public void AdvanceOneSecond()
    {
        if (_authoritativeSession is not null &&
            _authoritativeSession.Status != LocalFocusSessionStatusDto.Completed)
        {
            RefreshAuthoritativeTime(DateTimeOffset.UtcNow.AddSeconds(1));
            return;
        }

        if (Stage == FocusFlowStage.Preparing)
        {
            AdvancePreparationBy(TimeSpan.FromSeconds(1));
            return;
        }

        if (Stage == FocusFlowStage.Focusing)
        {
            SyncCompletedTaskIdsToEngine();
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

        SyncCompletedTaskIdsToEngine();
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
        if (_isExternalStarting && _authoritativeSession is null)
        {
            return;
        }

        if (_authoritativeSession is not null &&
            _authoritativeSession.Status != LocalFocusSessionStatusDto.Completed)
        {
            RefreshAuthoritativeTime(DateTimeOffset.UtcNow);
            return;
        }

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
        SyncCompletedTaskIdsToEngine();
        _engine.AdvanceFocusBy(focusElapsed);
        SyncFromEngine();
    }

    private void CancelPreparation()
    {
        if (Stage != FocusFlowStage.Preparing)
        {
            return;
        }

        if (_isExternalStarting)
        {
            var cancel = _cancelExternalStarting;
            _cancelExternalStarting = null;
            cancel?.Invoke();
            ResetToIdleCore();
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
        SyncCompletedTaskIdsToEngine();
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
        if (IsForcedModeActive && Stage is (FocusFlowStage.Preparing or FocusFlowStage.Focusing))
        {
            return;
        }

        _timer.Stop();
        ResetToIdleCore();
    }

    private void ResetToIdleCore()
    {
        _timer.Stop();
        _isExternalStarting = false;
        _cancelExternalStarting = null;
        _preparationStopwatch.Reset();
        _focusStopwatch.Reset();
        IsEndConfirmationOpen = false;
        IsForcedModeActive = false;
        _engine.ReturnHome();
        _authoritativeSessionId = null;
        _authoritativeSession = null;
        OnPropertyChanged(nameof(IsServiceOwnedForcedSession));
        OnPropertyChanged(nameof(AuthoritativeSessionId));
        _sessionTarget = null;
        SyncFromEngine();
        Stage = FocusFlowStage.Idle;
    }

    private void FocusAgain()
    {
        if (Stage != FocusFlowStage.Completed || IsServiceOwnedForcedSession)
        {
            return;
        }

        var configuredMinutes = _totalFocusSeconds / 60;
        Start(configuredMinutes, ActiveTarget, IsForcedModeActive && _canStartForcedMode());
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
            var removedFromSession = _sessionCompletedTaskSet.Remove(task);
            SessionCompletedTasks.Remove(task);
            OnPropertyChanged(nameof(SessionCompletedTaskCount));
            OnPropertyChanged(nameof(SessionCompletedTaskSummary));
            RefreshTaskGroups();
            if (removedFromSession && IsServiceOwnedForcedSession)
            {
                AuthoritativeTasksChanged?.Invoke(this, EventArgs.Empty);
            }
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
        if (!_applyingAuthoritativeSession && ActiveTarget is not null)
        {
            TargetTasksChanged?.Invoke(this, ActiveTarget);
        }
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
            if (!_applyingAuthoritativeSession && e.PropertyName != nameof(FocusTaskViewModel.IsEditing) && ActiveTarget is not null)
            {
                TargetTasksChanged?.Invoke(this, ActiveTarget);
            }
        }

        if (!_applyingAuthoritativeSession && IsServiceOwnedForcedSession &&
            (e.PropertyName == nameof(FocusTaskViewModel.IsCompleted) ||
             e.PropertyName == nameof(FocusTaskViewModel.Name) &&
             sender is FocusTaskViewModel renamedTask &&
             _sessionCompletedTaskSet.Contains(renamedTask)))
        {
            AuthoritativeTasksChanged?.Invoke(this, EventArgs.Empty);
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
            _sessionTarget?.AddFocusDuration(_engine.Completion.ActualDuration);
            CompletionRecorded?.Invoke(
                this,
                new FocusSessionCompletedEventArgs(
                    _engine.Completion,
                    SessionCompletedTasks.Select(task => task.Name).ToArray()));
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

    private void SyncCompletedTaskIdsToEngine()
    {
        _engine.UpdateCompletedTaskIds(SessionCompletedTasks.Select(task => task.TaskId));
    }

    private void RefreshAuthoritativeTime(DateTimeOffset nowUtc)
    {
        if (_authoritativeSession is not { } session)
        {
            return;
        }

        var current = nowUtc.ToUniversalTime();
        if (session.Status == LocalFocusSessionStatusDto.Completed)
        {
            PreparationSeconds = 0;
            PreparationProgress = 1;
            RemainingFocusSeconds = 0;
            Stage = FocusFlowStage.Completed;
            return;
        }

        if (session.Status == LocalFocusSessionStatusDto.Preparing &&
            session.FocusStartedAtUtc is { } focusStartsAt && current < focusStartsAt)
        {
            var preparationRemaining = focusStartsAt - current;
            PreparationSeconds = Math.Clamp((int)Math.Ceiling(preparationRemaining.TotalSeconds), 1, 5);
            PreparationProgress = Math.Clamp(
                1d - preparationRemaining.TotalSeconds / FocusSessionEngine.PreparationDuration.TotalSeconds,
                0d,
                1d);
            RemainingFocusSeconds = session.ConfiguredSeconds;
            Stage = FocusFlowStage.Preparing;
            return;
        }

        PreparationSeconds = 0;
        PreparationProgress = 1;
        var remaining = session.PlannedEndAtUtc is { } plannedEnd
            ? (int)Math.Ceiling((plannedEnd - current).TotalSeconds)
            : 0;
        RemainingFocusSeconds = Math.Clamp(remaining, 0, session.ConfiguredSeconds);
        Stage = FocusFlowStage.Focusing;
    }

    private void SynchronizeAuthoritativeCompletedTasks(LocalFocusSessionDto session)
    {
        _sessionCompletedTaskSet.Clear();
        SessionCompletedTasks.Clear();
        if (ActiveTarget is not null)
        {
            var completedIds = session.CompletedTasks.Select(task => task.TaskId).ToHashSet(StringComparer.Ordinal);
            var completedNames = session.CompletedTasks.Select(task => task.TaskNameSnapshot).ToHashSet(StringComparer.Ordinal);
            foreach (var task in ActiveTarget.Tasks)
            {
                var isCompleted = completedIds.Contains(task.TaskId) || completedNames.Contains(task.Name);
                if (isCompleted)
                {
                    task.IsCompleted = true;
                    _sessionCompletedTaskSet.Add(task);
                    SessionCompletedTasks.Add(task);
                }
            }
        }

        OnPropertyChanged(nameof(SessionCompletedTaskCount));
        OnPropertyChanged(nameof(SessionCompletedTaskSummary));
        RefreshTaskGroups();
    }

    private void RecordAuthoritativeCompletion(LocalFocusSessionDto session)
    {
        if (_lastAuthoritativeCompletionSessionId == session.SessionId ||
            session.CompletedAtUtc is null ||
            session.CompletionKind is null)
        {
            return;
        }

        _lastAuthoritativeCompletionSessionId = session.SessionId;
        _completedFocusSeconds = session.ActualSeconds;
        _todayTotalSeconds += _completedFocusSeconds;
        _completedAt = session.CompletedAtUtc.Value.LocalDateTime;
        var record = new FocusSessionRecord(
            TimeSpan.FromSeconds(session.ConfiguredSeconds),
            TimeSpan.FromSeconds(session.ActualSeconds),
            session.PreparationStartedAtUtc.LocalDateTime,
            session.CompletedAtUtc.Value.LocalDateTime,
            (FocusCompletionKind)session.CompletionKind.Value,
            true)
        {
            TargetId = session.TargetId,
            TargetName = session.TargetNameSnapshot,
            CompletedTaskIds = session.CompletedTasks.Select(task => task.TaskId).ToArray()
        };
        _completionHistory.Add(record);
        _sessionTarget?.AddFocusDuration(record.ActualDuration);
        CompletionRecorded?.Invoke(
            this,
            new FocusSessionCompletedEventArgs(
                record,
                session.CompletedTasks.Select(task => task.TaskNameSnapshot).ToArray()));
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

public sealed class FocusSessionCompletedEventArgs(
    FocusSessionRecord record,
    IReadOnlyList<string> completedTaskNames) : EventArgs
{
    public FocusSessionRecord Record { get; } = record;

    public IReadOnlyList<string> CompletedTaskNames { get; } = completedTaskNames;
}
