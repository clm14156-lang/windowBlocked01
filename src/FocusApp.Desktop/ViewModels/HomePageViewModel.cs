using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomePageViewModel : INotifyPropertyChanged
{
    private readonly HomeDurationOptionViewModel _customDurationOption;
    // Keep selection age independent of the numerically sorted shortcuts and common-time list.
    private readonly List<HomeDurationOptionViewModel> _selectionOrder = [];
    private HomeDurationOptionViewModel _currentDurationOption;
    private AutomaticRuleItemViewModel? _nextAutomaticRule;
    private DateTime _nextAutomaticStart;
    private string _nextAutomaticCountdownDisplay = string.Empty;
    private readonly AutomaticBlockingScheduler _automaticBlockingScheduler;
    private int _enabledBlockingCount;
    private bool _isLoggedIn;
    private bool _isVip;
    private bool _isForcedModeRequested;
    private FocusStartModeDecision _lastFocusStartDecision = FocusStartModeDecision.Normal;
    private Func<int, FocusTargetViewModel?, Guid?, DateTimeOffset?, Task<bool>>? _forcedFocusStarter;
    private Func<Task>? _forcedFocusStartCanceller;
    private bool _isStartingForcedFocus;
    private bool _isApplyingPersistedDurations;
    private bool _isUpdatingDurationSelection;
    private string _focusStartError = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DurationOptionsChanged;
    public event EventHandler? BlockingPageRequested;
    public event Action<Guid>? ManageAutomaticRuleRequested;

    public HomePageViewModel(
        IEnumerable<HomeDurationOptionViewModel> durationOptions,
        AutomaticBlockingScheduler? automaticBlockingScheduler = null,
        FocusSessionViewModel? focusSession = null,
        FocusTargetModalViewModel? focusTargetModal = null)
    {
        _automaticBlockingScheduler = automaticBlockingScheduler ?? new AutomaticBlockingScheduler();
        var suppliedOptions = durationOptions.ToList();
        _customDurationOption = suppliedOptions.FirstOrDefault(option => !string.IsNullOrEmpty(option.Icon))
            ?? new HomeDurationOptionViewModel("自定义", "\uE823");
        var commonOptions = suppliedOptions.Where(option => !ReferenceEquals(option, _customDurationOption)).ToList();
        DurationOptions = new ObservableCollection<HomeDurationOptionViewModel>(commonOptions) { _customDurationOption };
        VisibleDurationOptions = new ObservableCollection<HomeDurationOptionViewModel>();

        if (DurationOptions.Count == 0)
        {
            throw new ArgumentException("At least one duration option is required.", nameof(durationOptions));
        }

        if (!DurationOptions.Any(option => option.IsSelected))
        {
            DurationOptions[0].IsSelected = true;
        }

        foreach (var option in commonOptions.Where(option => option.IsSelected).Skip(4)) option.IsSelected = false;
        _currentDurationOption = commonOptions.FirstOrDefault(option => option.IsSelected) ?? commonOptions.FirstOrDefault() ?? _customDurationOption;
        _currentDurationOption.IsCurrent = true;
        _selectionOrder.AddRange(DurationOptions.Where(option => option.IsSelected && !ReferenceEquals(option, _customDurationOption)));

        foreach (var option in DurationOptions)
        {
            option.PropertyChanged += DurationOption_PropertyChanged;
        }
        RefreshVisibleDurationOptions();

        CustomTimeModal = new CustomTimeModalViewModel(ConfirmCustomTime, commonOptions, SaveCommonTimeSelection, DeleteCommonTime,
            () => _selectionOrder.Where(option => option.IsSelected));
        FocusTargetModal = focusTargetModal ?? new FocusTargetModalViewModel();
        FocusTargetModal.StartFocusRequested += FocusTargetModal_StartFocusRequested;
        FocusSession = focusSession ?? new FocusSessionViewModel();
        FocusSession.SetForcedModeStartPermission(() =>
            ForcedModeAccessPolicy.EvaluateStart(_isLoggedIn, _isVip, _isForcedModeRequested) ==
            FocusStartModeDecision.Forced);
        BlockedContentModal = new BlockedContentModalViewModel();
        SelectDurationCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectDuration);
        StartFocusCommand = new RelayCommand<object>(_ => StartFocus());
        OpenFocusTargetCommand = new RelayCommand<object>(_ => FocusTargetModal.Open());
        OpenBlockedContentCommand = new RelayCommand<object>(_ => OpenBlockedContent());
        ManageNextAutomaticRuleCommand = new RelayCommand<object>(_ => ManageNextAutomaticRule());
    }

    public ObservableCollection<HomeDurationOptionViewModel> DurationOptions { get; }

    public ObservableCollection<HomeDurationOptionViewModel> VisibleDurationOptions { get; }

    public HomeDurationOptionViewModel CurrentDurationOption => _currentDurationOption;

    public ICommand SelectDurationCommand { get; }

    public ICommand StartFocusCommand { get; }

    public ICommand OpenFocusTargetCommand { get; }

    public ICommand OpenBlockedContentCommand { get; }

    public ICommand ManageNextAutomaticRuleCommand { get; }

    public CustomTimeModalViewModel CustomTimeModal { get; }

    public FocusTargetModalViewModel FocusTargetModal { get; }

    public FocusSessionViewModel FocusSession { get; }

    public BlockedContentModalViewModel BlockedContentModal { get; }

    public void ApplyDurationPresets(IEnumerable<LocalDurationPresetDto> presets)
    {
        var ordered = presets.OrderBy(preset => preset.SortOrder).ToList();
        if (ordered.Count == 0) return;
        _isApplyingPersistedDurations = true;
        try
        {
            foreach (var option in DurationOptions.Where(option => !ReferenceEquals(option, _customDurationOption)).ToList())
            {
                option.PropertyChanged -= DurationOption_PropertyChanged;
                DurationOptions.Remove(option);
            }
            _selectionOrder.Clear();
            foreach (var preset in ordered)
            {
                var option = new HomeDurationOptionViewModel($"{preset.Minutes} 分钟", string.Empty, preset.IsVisible && _selectionOrder.Count < 4, preset.Minutes)
                {
                    IsCurrent = preset.IsCurrent
                };
                option.PropertyChanged += DurationOption_PropertyChanged;
                DurationOptions.Insert(Math.Max(0, DurationOptions.Count - 1), option);
                if (option.IsSelected) _selectionOrder.Add(option);
            }
            var currentPreset = ordered.FirstOrDefault(preset => preset.IsCurrent) ?? ordered[0];
            SelectOnly(DurationOptions.First(option => option.Minutes == currentPreset.Minutes));
            RefreshVisibleDurationOptions();
            OnPropertyChanged(nameof(DurationOptions));
            OnPropertyChanged(nameof(CurrentDurationOption));
            CustomTimeModal.CommonTimes.Clear();
            foreach (var option in DurationOptions.Where(option => !ReferenceEquals(option, _customDurationOption)).OrderBy(option => option.Minutes))
                CustomTimeModal.CommonTimes.Add(option);
        }
        finally { _isApplyingPersistedDurations = false; }
    }

    public FocusStartModeDecision LastFocusStartDecision
    {
        get => _lastFocusStartDecision;
        private set
        {
            if (_lastFocusStartDecision == value)
            {
                return;
            }

            _lastFocusStartDecision = value;
            OnPropertyChanged();
        }
    }

    public bool IsForcedModeRequested => _isForcedModeRequested;

    public bool IsStartingForcedFocus
    {
        get => _isStartingForcedFocus;
        private set
        {
            if (_isStartingForcedFocus == value) return;
            _isStartingForcedFocus = value;
            OnPropertyChanged();
        }
    }

    public string FocusStartError
    {
        get => _focusStartError;
        private set
        {
            if (_focusStartError == value) return;
            _focusStartError = value;
            OnPropertyChanged();
        }
    }

    public void SetForcedFocusStarter(
        Func<int, FocusTargetViewModel?, Guid?, DateTimeOffset?, Task<bool>> starter)
        => _forcedFocusStarter = starter ?? throw new ArgumentNullException(nameof(starter));

    public void SetForcedFocusStartCanceller(Func<Task> canceller)
        => _forcedFocusStartCanceller = canceller ?? throw new ArgumentNullException(nameof(canceller));

    public void SetUserAccess(bool isLoggedIn, bool isVip)
    {
        if (_isLoggedIn == isLoggedIn && _isVip == isVip)
        {
            return;
        }

        _isLoggedIn = isLoggedIn;
        _isVip = isVip;
    }

    public void SetForcedModeEnabled(bool enabled)
    {
        if (_isForcedModeRequested == enabled)
        {
            return;
        }

        _isForcedModeRequested = enabled;
        OnPropertyChanged(nameof(IsForcedModeRequested));
    }

    public ObservableCollection<BlockingContentItemViewModel> BlockingPreviewItems { get; } = [];

    public int EnabledBlockingCount
    {
        get => _enabledBlockingCount;
        private set
        {
            if (_enabledBlockingCount == value) return;
            _enabledBlockingCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBlockingContent));
            OnPropertyChanged(nameof(AdditionalBlockingCount));
            OnPropertyChanged(nameof(HasAdditionalBlockingItems));
            OnPropertyChanged(nameof(BlockingCountText));
            OnPropertyChanged(nameof(NextAutomaticBlockedContentDisplay));
        }
    }

    public bool HasBlockingContent => EnabledBlockingCount > 0;

    public int AdditionalBlockingCount => Math.Max(0, EnabledBlockingCount - 3);

    public bool HasAdditionalBlockingItems => AdditionalBlockingCount > 0;

    public string BlockingCountText => $"已屏蔽 {EnabledBlockingCount} 个网站和应用";

    public bool HasNextAutomaticRule => _nextAutomaticRule is not null;

    public string NextAutomaticBlockingDisplay => _nextAutomaticRule is null
        ? ""
        : $"自动屏蔽 · {_nextAutomaticStart:HH:mm}";

    public string NextAutomaticStartDisplay => _nextAutomaticRule is null
        ? ""
        : _nextAutomaticStart.ToString("HH:mm");

    public string NextAutomaticTimeRangeDisplay => _nextAutomaticRule?.TimeRangeText ?? string.Empty;

    public bool NextAutomaticHasCustomDays => _nextAutomaticRule?.IsCustom == true;

    public string NextAutomaticWeekdaysDisplay => _nextAutomaticRule is null || !_nextAutomaticRule.IsCustom
        ? string.Empty
        : string.Join(" · ", new[]
        {
            ("Monday", "周一"), ("Tuesday", "周二"), ("Wednesday", "周三"),
            ("Thursday", "周四"), ("Friday", "周五"), ("Saturday", "周六"), ("Sunday", "周日")
        }.Where(day => _nextAutomaticRule.DayKeys.Contains(day.Item1)).Select(day => day.Item2));

    public string NextAutomaticCountdownDisplay => _nextAutomaticCountdownDisplay;

    public string NextAutomaticBlockedContentDisplay => $"将屏蔽 {EnabledBlockingCount} 个网站和应用";

    public void RefreshNextAutomaticRuleCountdown(DateTime? now = null)
    {
        var minutes = _nextAutomaticRule is null
            ? 0
            : Math.Max(0, (int)Math.Ceiling((_nextAutomaticStart - (now ?? DateTime.Now)).TotalMinutes));
        var display = _nextAutomaticRule is null ? string.Empty : minutes switch
        {
            0 => "即将开始",
            < 60 => $"{minutes}分钟后",
            < 1440 when minutes % 60 == 0 => $"{minutes / 60}小时后",
            < 1440 => $"{minutes / 60}小时{minutes % 60}分钟后",
            _ => $"{(int)Math.Ceiling(minutes / 1440d)}天后"
        };
        if (_nextAutomaticCountdownDisplay == display) return;
        _nextAutomaticCountdownDisplay = display;
        OnPropertyChanged(nameof(NextAutomaticCountdownDisplay));
    }

    public string NextAutomaticBlockingToolTip => _nextAutomaticRule is null
        ? ""
        : $"{_nextAutomaticStart:HH:mm} 开始自动屏蔽，持续 {FormatRuleDuration(_nextAutomaticRule)}";

    private void ManageNextAutomaticRule()
    {
        if (_nextAutomaticRule is not null)
        {
            ManageAutomaticRuleRequested?.Invoke(_nextAutomaticRule.Id);
        }
    }

    public void UpdateAutomaticRules(IEnumerable<AutomaticRuleItemViewModel> rules, DateTime? now = null)
        => UpdateAutomaticRules(rules, true, now);

    public void UpdateAutomaticRules(
        IEnumerable<AutomaticRuleItemViewModel> rules,
        bool isAutomaticBlockingEnabled,
        DateTime? now = null)
    {
        var current = now ?? DateTime.Now;
        _nextAutomaticRule = null;
        _nextAutomaticStart = default;

        if (!isAutomaticBlockingEnabled)
        {
            OnPropertyChanged(nameof(HasNextAutomaticRule));
            OnPropertyChanged(nameof(NextAutomaticBlockingDisplay));
            OnPropertyChanged(nameof(NextAutomaticStartDisplay));
            OnPropertyChanged(nameof(NextAutomaticBlockingToolTip));
            NotifyNextAutomaticRuleDetailsChanged(current);
            return;
        }

        foreach (var rule in rules.Where(item => item.IsEnabled))
        {
            var candidate = AutomaticBlockingSchedule.GetOccurrenceStartingOn(
                AutomaticBlockingRuleMapper.ToRule(rule),
                current.Date);
            if (candidate is null || candidate.StartsAt <= current)
            {
                continue;
            }

            if (_nextAutomaticRule is null || candidate.StartsAt < _nextAutomaticStart)
            {
                _nextAutomaticRule = rule;
                _nextAutomaticStart = candidate.StartsAt;
            }
        }

        OnPropertyChanged(nameof(HasNextAutomaticRule));
        OnPropertyChanged(nameof(NextAutomaticBlockingDisplay));
        OnPropertyChanged(nameof(NextAutomaticStartDisplay));
        OnPropertyChanged(nameof(NextAutomaticBlockingToolTip));
        NotifyNextAutomaticRuleDetailsChanged(current);
    }

    private void NotifyNextAutomaticRuleDetailsChanged(DateTime current)
    {
        OnPropertyChanged(nameof(NextAutomaticTimeRangeDisplay));
        OnPropertyChanged(nameof(NextAutomaticHasCustomDays));
        OnPropertyChanged(nameof(NextAutomaticWeekdaysDisplay));
        RefreshNextAutomaticRuleCountdown(current);
    }

    public void UpdateBlockingContent(
        IEnumerable<BlockingWebsiteItemViewModel> websites,
        IEnumerable<BlockingApplicationItemViewModel> applications)
    {
        var websiteList = websites.ToList();
        var applicationList = applications.ToList();
        var activeItems = websiteList.Where(item => item.IsEnabled).Select(item => new BlockingContentItemViewModel(item))
            .Concat(applicationList.Where(item => item.IsEnabled).Select(item => new BlockingContentItemViewModel(item)))
            .OrderByDescending(item => item.Favicon is not null)
            .ToList();

        foreach (var item in BlockingPreviewItems) item.Dispose();
        BlockingPreviewItems.Clear();
        foreach (var item in activeItems.Take(3)) BlockingPreviewItems.Add(item);
        foreach (var item in activeItems.Skip(3)) item.Dispose();

        EnabledBlockingCount = activeItems.Count;
        BlockedContentModal.Update(websiteList, applicationList);
    }

    /// <summary>
    /// Evaluates the current automatic-blocking window and starts the same focus
    /// flow used by the Start Focus button when a new window begins.
    /// </summary>
    public void EvaluateAutomaticBlocking(
        IEnumerable<AutomaticRuleItemViewModel> rules,
        bool isAutomaticBlockingEnabled,
        DateTime? now = null)
    {
        var current = now ?? DateTime.Now;
        var ruleList = rules.ToList();
        var evaluation = _automaticBlockingScheduler.Evaluate(
            ruleList.Select(AutomaticBlockingRuleMapper.ToRule),
            isAutomaticBlockingEnabled,
            current);

        UpdateAutomaticRules(ruleList, isAutomaticBlockingEnabled, current);
        if (evaluation.StartRequest is not null && !FocusSession.IsActive)
        {
            StartFocus(
                evaluation.StartRequest.FocusMinutes,
                evaluation.StartRequest.RuleId,
                new DateTimeOffset(evaluation.StartRequest.OccurrenceStartsAt).ToUniversalTime(),
                FocusTargetModal.Targets.FirstOrDefault(target => target.TargetId == ruleList.FirstOrDefault(rule => rule.Id == evaluation.StartRequest.RuleId)?.TargetId));
        }
    }

    private void SelectDuration(HomeDurationOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        if (ReferenceEquals(option, _customDurationOption))
        {
            CustomTimeModal.Open();
            return;
        }

        SelectOnly(option);
    }

    private void OpenBlockedContent()
    {
        if (HasBlockingContent)
        {
            BlockedContentModal.Open();
            return;
        }

        BlockingPageRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool ConfirmCustomTime(int minutes)
    {
        if (minutes is < 5 or > 480 || DurationOptions.Count(item => !ReferenceEquals(item, _customDurationOption)) >= 9 ||
            DurationOptions.Any(option => option.Minutes == minutes && !ReferenceEquals(option, _customDurationOption)))
        {
            return false;
        }

        var option = new HomeDurationOptionViewModel($"{minutes} 分钟", string.Empty, false, minutes);
        DurationOptions.Insert(Math.Max(0, DurationOptions.Count - 1), option);
        CustomTimeModal.AddCommonTime(option);
        option.PropertyChanged += DurationOption_PropertyChanged;
        OnPropertyChanged(nameof(DurationOptions));
        NotifyDurationOptionsChanged();
        return true;
    }

    private void StartFocus(
        int? minutes = null,
        Guid? automaticRuleId = null,
        DateTimeOffset? automaticOccurrenceStartedAtUtc = null,
        FocusTargetViewModel? automaticTarget = null)
    {
        var decision = ForcedModeAccessPolicy.EvaluateStart(
            _isLoggedIn,
            _isVip,
            _isForcedModeRequested);
        LastFocusStartDecision = decision;
        if (!ForcedModeAccessPolicy.CanStart(decision))
        {
            return;
        }

        var selectedDuration = _currentDurationOption;
        var focusMinutes = minutes ?? selectedDuration.Minutes;
        var target = automaticTarget ?? (FocusTargetModal.HasSelectedTarget ? FocusTargetModal.SelectedTarget : null);
        if (decision == FocusStartModeDecision.Forced && _forcedFocusStarter is not null)
        {
            if (IsStartingForcedFocus ||
                (FocusSession.IsServiceOwnedForcedSession && FocusSession.IsPreparing))
            {
                _ = _forcedFocusStartCanceller?.Invoke();
            }
            else
            {
                _ = StartForcedFocusAsync(
                    focusMinutes,
                    target,
                    automaticRuleId,
                    automaticOccurrenceStartedAtUtc);
            }
            return;
        }

        FocusSession.Start(
            focusMinutes,
            target,
            decision == FocusStartModeDecision.Forced);
    }

    private void FocusTargetModal_StartFocusRequested(FocusTargetViewModel target)
        => StartFocus(automaticTarget: target);

    private async Task StartForcedFocusAsync(
        int minutes,
        FocusTargetViewModel? target,
        Guid? automaticRuleId,
        DateTimeOffset? automaticOccurrenceStartedAtUtc)
    {
        IsStartingForcedFocus = true;
        FocusStartError = string.Empty;
        FocusSession.BeginForcedFocusStarting(minutes, target, () => _ = _forcedFocusStartCanceller?.Invoke());
        try
        {
            if (!await _forcedFocusStarter!(
                    minutes,
                    target,
                    automaticRuleId,
                    automaticOccurrenceStartedAtUtc))
            {
                FocusStartError = "后台未能启动强制专注。";
                FocusSession.ClearForcedFocusStarting();
            }
        }
        catch (OperationCanceledException)
        {
            FocusSession.ClearForcedFocusStarting();
        }
        catch (Exception exception)
        {
            FocusStartError = exception.Message;
            FocusSession.ClearForcedFocusStarting();
        }
        finally
        {
            IsStartingForcedFocus = false;
        }
    }

    private void SelectOnly(HomeDurationOptionViewModel option)
    {
        if (ReferenceEquals(option, _customDurationOption))
        {
            return;
        }

        foreach (var durationOption in DurationOptions)
        {
            durationOption.IsCurrent = ReferenceEquals(durationOption, option);
        }
        _currentDurationOption = option;
        OnPropertyChanged(nameof(CurrentDurationOption));
        NotifyDurationOptionsChanged();
    }

    private void SaveCommonTimeSelection(IReadOnlyList<int> selectedMinutes, int preferredMinutes)
    {
        _isUpdatingDurationSelection = true;
        try
        {
            _selectionOrder.Clear();
            foreach (var minutes in selectedMinutes.Distinct().Take(4))
            {
                var option = DurationOptions.FirstOrDefault(item => item.Minutes == minutes && string.IsNullOrEmpty(item.Icon));
                if (option is not null) _selectionOrder.Add(option);
            }
            foreach (var option in DurationOptions)
                option.IsSelected = _selectionOrder.Contains(option);
            var current = _selectionOrder.FirstOrDefault(option => option.Minutes == preferredMinutes)
                ?? _selectionOrder.FirstOrDefault(option => ReferenceEquals(option, _currentDurationOption))
                ?? _selectionOrder.FirstOrDefault();
            if (current is not null) SelectOnly(current);
        }
        finally
        {
            _isUpdatingDurationSelection = false;
        }
        RefreshVisibleDurationOptions();
        NotifyDurationOptionsChanged();
    }

    private void DeleteCommonTime(int minutes)
    {
        var option = DurationOptions.FirstOrDefault(item => item.Minutes == minutes && !ReferenceEquals(item, _customDurationOption));
        if (option is null) return;
        DurationOptions.Remove(option);
        _selectionOrder.Remove(option);
        option.PropertyChanged -= DurationOption_PropertyChanged;
        if (ReferenceEquals(option, _currentDurationOption))
        {
            var current = _selectionOrder.FirstOrDefault() ?? DurationOptions.FirstOrDefault(item => item.Icon.Length == 0);
            if (current is not null)
            {
                _isUpdatingDurationSelection = true;
                try { SelectOnly(current); }
                finally { _isUpdatingDurationSelection = false; }
            }
        }
        OnPropertyChanged(nameof(DurationOptions));
        RefreshVisibleDurationOptions();
        DurationOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DurationOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeDurationOptionViewModel.IsSelected) && !_isUpdatingDurationSelection && !_isApplyingPersistedDurations)
        {
            RefreshVisibleDurationOptions();
            NotifyDurationOptionsChanged();
        }
    }

    private void NotifyDurationOptionsChanged()
    {
        if (!_isApplyingPersistedDurations && !_isUpdatingDurationSelection)
        {
            DurationOptionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshVisibleDurationOptions()
    {
        VisibleDurationOptions.Clear();
        foreach (var option in _selectionOrder.Where(item => item.IsSelected).Take(4).OrderBy(item => item.Minutes))
        {
            VisibleDurationOptions.Add(option);
        }
        VisibleDurationOptions.Add(_customDurationOption);
        OnPropertyChanged(nameof(VisibleDurationOptions));
    }

    public IReadOnlyList<LocalDurationPresetDto> GetDurationPresets()
        // Persist selection age so FIFO replacement still works after a restart.
        => _selectionOrder.Where(option => option.IsSelected)
            .Concat(DurationOptions.Where(option => option.Icon.Length == 0 && !option.IsSelected))
            .Select((option, index) => new LocalDurationPresetDto(
                Guid.Parse($"00000000-0000-0000-0000-{option.Minutes:D12}"), option.Minutes, option.IsSelected,
                ReferenceEquals(option, _currentDurationOption), index)).ToArray();

    private static string FormatRuleDuration(AutomaticRuleItemViewModel rule)
    {
        var minutes = (int)Math.Round(rule.EndMinutes - rule.StartMinutes);
        if (minutes < 0)
        {
            minutes += 24 * 60;
        }
        return minutes % 60 == 0 ? $"{minutes / 60} 小时" : $"{minutes / 60} 小时 {minutes % 60} 分钟";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
