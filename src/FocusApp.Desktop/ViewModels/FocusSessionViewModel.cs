using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusSessionViewModel : INotifyPropertyChanged
{
    private const int PreparationDurationSeconds = 5;
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _nowProvider;
    private readonly Stopwatch _preparationStopwatch = new();
    private FocusFlowStage _stage;
    private int _preparationSeconds = PreparationDurationSeconds;
    private double _preparationProgress;
    private TimeSpan _manualPreparationElapsed;
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

    public FocusSessionViewModel(Func<DateTime>? nowProvider = null, bool runTimer = true)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
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
        if (minutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        _timer.Stop();
        _preparationStopwatch.Reset();
        _manualPreparationElapsed = TimeSpan.Zero;
        PreparationSeconds = PreparationDurationSeconds;
        PreparationProgress = 0;
        _totalFocusSeconds = checked(minutes * 60);
        RemainingFocusSeconds = _totalFocusSeconds;
        IsEndConfirmationOpen = false;
        IsForcedModeActive = forcedMode;
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

        AdvanceFocusOneSecond();
    }

    public void AdvancePreparationBy(TimeSpan elapsed)
    {
        if (Stage != FocusFlowStage.Preparing || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _manualPreparationElapsed += elapsed;
        UpdatePreparation(_manualPreparationElapsed);
    }

    private void OnTimerTick()
    {
        if (Stage == FocusFlowStage.Preparing)
        {
            UpdatePreparation(_preparationStopwatch.Elapsed);
            return;
        }

        AdvanceFocusOneSecond();
    }

    private void AdvanceFocusOneSecond()
    {

        if (Stage != FocusFlowStage.Focusing || IsEndConfirmationOpen)
        {
            return;
        }

        if (RemainingFocusSeconds > 1)
        {
            RemainingFocusSeconds--;
            return;
        }

        RemainingFocusSeconds = 0;
        CompleteFocus();
    }

    private void UpdatePreparation(TimeSpan elapsed)
    {
        var elapsedSeconds = elapsed.TotalSeconds;
        PreparationProgress = Math.Clamp(elapsedSeconds / PreparationDurationSeconds, 0d, 1d);

        if (elapsedSeconds >= PreparationDurationSeconds)
        {
            BeginFocus();
            return;
        }

        PreparationSeconds = Math.Clamp(
            (int)Math.Ceiling(PreparationDurationSeconds - elapsedSeconds),
            1,
            PreparationDurationSeconds);
    }

    private void BeginFocus()
    {
        _preparationStopwatch.Stop();
        PreparationProgress = 1;
        RemainingFocusSeconds = _totalFocusSeconds;
        Stage = FocusFlowStage.Focusing;
        if (RunTimer)
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
        }
    }

    private void CancelPreparation()
    {
        if (Stage != FocusFlowStage.Preparing)
        {
            return;
        }

        _timer.Stop();
        _preparationStopwatch.Reset();
        IsForcedModeActive = false;
        Stage = FocusFlowStage.Idle;
    }

    private void OpenEndConfirmation()
    {
        if (Stage != FocusFlowStage.Focusing || IsEndConfirmationOpen || IsForcedModeActive)
        {
            return;
        }

        IsEndConfirmationOpen = true;
        _timer.Stop();
    }

    private void ContinueFocus()
    {
        if (!IsEndConfirmationOpen)
        {
            return;
        }

        IsEndConfirmationOpen = false;
        StartTimerIfEnabled();
    }

    private void CompleteFocus()
    {
        if (Stage != FocusFlowStage.Focusing)
        {
            return;
        }

        _timer.Stop();
        _preparationStopwatch.Reset();
        IsEndConfirmationOpen = false;
        _completedFocusSeconds = _totalFocusSeconds - RemainingFocusSeconds;
        _todayTotalSeconds += _completedFocusSeconds;
        _completedAt = _nowProvider();
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
        IsEndConfirmationOpen = false;
        IsForcedModeActive = false;
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
        BeginFocus();
    }

    private void AddTask()
    {
        if (!HasTarget)
        {
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
            _timer.Start();
        }
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
