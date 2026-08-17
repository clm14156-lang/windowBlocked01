using System.ComponentModel;
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
        FocusAgainCommand = new RelayCommand<object>(_ => ReturnHome());
        ReturnHomeCommand = new RelayCommand<object>(_ => ReturnHome());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CancelPreparationCommand { get; }

    public ICommand RequestEndCommand { get; }

    public ICommand ContinueFocusCommand { get; }

    public ICommand ConfirmEndCommand { get; }

    public ICommand FocusAgainCommand { get; }

    public ICommand ReturnHomeCommand { get; }

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

    public void Start(int minutes)
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
        Stage = FocusFlowStage.Idle;
    }

    private void OpenEndConfirmation()
    {
        if (Stage != FocusFlowStage.Focusing || IsEndConfirmationOpen)
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
        OnPropertyChanged(nameof(TodayTotalDisplay));
        OnPropertyChanged(nameof(CompletedAtDisplay));
        Stage = FocusFlowStage.Completed;
    }

    private void ReturnHome()
    {
        _timer.Stop();
        IsEndConfirmationOpen = false;
        Stage = FocusFlowStage.Idle;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
