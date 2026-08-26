namespace FocusApp.Core;

public sealed class FocusSessionEngine
{
    public static readonly TimeSpan PreparationDuration = TimeSpan.FromSeconds(5);

    private readonly Func<DateTime> _nowProvider;
    private TimeSpan _preparationElapsed;
    private TimeSpan _focusElapsed;
    private DateTime _startedAt;
    private DateTime _focusStartedAt;
    private FocusSessionTargetContext? _targetContext;
    private readonly List<string> _completedTaskIds = [];

    public FocusSessionEngine(Func<DateTime>? nowProvider = null)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public FocusSessionState State { get; private set; }

    public bool IsForcedMode { get; private set; }

    public bool IsEndConfirmationOpen { get; private set; }

    public int ConfiguredSeconds { get; private set; }

    public int PreparationSeconds => State == FocusSessionState.Preparing
        ? Math.Clamp((int)Math.Ceiling((PreparationDuration - _preparationElapsed).TotalSeconds), 1, 5)
        : State == FocusSessionState.Idle ? 5 : 0;

    public double PreparationProgress => State == FocusSessionState.Preparing
        ? Math.Clamp(_preparationElapsed.TotalSeconds / PreparationDuration.TotalSeconds, 0d, 1d)
        : State is FocusSessionState.Focusing or FocusSessionState.Completed ? 1d : 0d;

    public int RemainingFocusSeconds => ConfiguredSeconds == 0
        ? 0
        : Math.Clamp((int)Math.Ceiling(ConfiguredSeconds - _focusElapsed.TotalSeconds), 0, ConfiguredSeconds);

    public int ElapsedFocusSeconds => Math.Clamp(ConfiguredSeconds - RemainingFocusSeconds, 0, ConfiguredSeconds);

    public DateTime? StartedAt => State == FocusSessionState.Idle ? null : _startedAt;

    public DateTime? FocusStartedAt => State is FocusSessionState.Focusing or FocusSessionState.Completed
        ? _focusStartedAt
        : null;

    public DateTime? CompletedAt { get; private set; }

    public FocusSessionRecord? Completion { get; private set; }

    public string? TargetId => _targetContext?.TargetId;

    public string? TargetName => _targetContext?.TargetName;

    public IReadOnlyList<string> CompletedTaskIds => _completedTaskIds.ToArray();

    public void Start(int minutes, bool forcedMode = false, FocusSessionTargetContext? target = null)
    {
        if (minutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        ConfiguredSeconds = checked(minutes * 60);
        IsForcedMode = forcedMode;
        _targetContext = target;
        _completedTaskIds.Clear();
        IsEndConfirmationOpen = false;
        _preparationElapsed = TimeSpan.Zero;
        _focusElapsed = TimeSpan.Zero;
        _startedAt = _nowProvider();
        _focusStartedAt = default;
        CompletedAt = null;
        Completion = null;
        State = FocusSessionState.Preparing;
    }

    public void Start(int minutes, FocusSessionTargetContext targetContext, bool forcedMode = false)
    {
        Start(minutes, forcedMode, targetContext);
    }

    public void UpdateCompletedTaskIds(IEnumerable<string>? taskIds)
    {
        if (State is not (FocusSessionState.Preparing or FocusSessionState.Focusing))
        {
            return;
        }

        _completedTaskIds.Clear();
        if (taskIds is null)
        {
            return;
        }

        foreach (var taskId in taskIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (!_completedTaskIds.Contains(taskId, StringComparer.Ordinal))
            {
                _completedTaskIds.Add(taskId);
            }
        }
    }

    public bool AdvancePreparationBy(TimeSpan elapsed)
    {
        if (State != FocusSessionState.Preparing || elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        _preparationElapsed += elapsed;
        if (_preparationElapsed < PreparationDuration)
        {
            return true;
        }

        var focusElapsed = _preparationElapsed - PreparationDuration;
        BeginFocus();
        if (focusElapsed > TimeSpan.Zero)
        {
            AdvanceFocusBy(focusElapsed);
        }

        return true;
    }

    public bool AdvanceFocusBy(TimeSpan elapsed)
    {
        if (State != FocusSessionState.Focusing || IsEndConfirmationOpen || elapsed <= TimeSpan.Zero)
        {
            return false;
        }

        _focusElapsed = TimeSpan.FromSeconds(Math.Min(
            ConfiguredSeconds,
            _focusElapsed.TotalSeconds + elapsed.TotalSeconds));

        if (_focusElapsed.TotalSeconds >= ConfiguredSeconds)
        {
            Complete(FocusCompletionKind.Natural);
        }

        return true;
    }

    public bool CancelPreparation()
    {
        if (State != FocusSessionState.Preparing)
        {
            return false;
        }

        Reset();
        return true;
    }

    public bool RequestEnd()
    {
        if (State != FocusSessionState.Focusing || IsEndConfirmationOpen || IsForcedMode)
        {
            return false;
        }

        IsEndConfirmationOpen = true;
        return true;
    }

    public bool ContinueFocus()
    {
        if (!IsEndConfirmationOpen)
        {
            return false;
        }

        IsEndConfirmationOpen = false;
        return true;
    }

    public bool ConfirmEnd()
    {
        if (State != FocusSessionState.Focusing || IsForcedMode)
        {
            return false;
        }

        Complete(FocusCompletionKind.EarlyEnd);
        return true;
    }

    public void ReturnHome()
    {
        Reset();
    }

    private void BeginFocus()
    {
        _preparationElapsed = PreparationDuration;
        _focusElapsed = TimeSpan.Zero;
        _focusStartedAt = _nowProvider();
        State = FocusSessionState.Focusing;
    }

    private void Complete(FocusCompletionKind completionKind)
    {
        if (State != FocusSessionState.Focusing)
        {
            return;
        }

        var completedAt = _nowProvider();
        CompletedAt = completedAt;
        IsEndConfirmationOpen = false;
        Completion = new FocusSessionRecord(
            TimeSpan.FromSeconds(ConfiguredSeconds),
            TimeSpan.FromSeconds(ElapsedFocusSeconds),
            _startedAt,
            completedAt,
            completionKind,
            IsForcedMode)
        {
            TargetId = _targetContext?.TargetId,
            TargetName = _targetContext?.TargetName,
            CompletedTaskIds = _completedTaskIds.ToArray()
        };
        State = FocusSessionState.Completed;
    }

    private void Reset()
    {
        State = FocusSessionState.Idle;
        IsForcedMode = false;
        IsEndConfirmationOpen = false;
        _preparationElapsed = TimeSpan.Zero;
        _focusElapsed = TimeSpan.Zero;
        _targetContext = null;
        _completedTaskIds.Clear();
        CompletedAt = null;
        Completion = null;
    }
}
