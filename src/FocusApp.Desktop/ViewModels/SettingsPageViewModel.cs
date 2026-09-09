using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Contracts;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class SettingsPageViewModel : INotifyPropertyChanged
{
    private SettingsEntryItemViewModel? _activeEntry;
    private AutomaticRuleItemViewModel? _editingRule;
    private bool _isLoggedIn;
    private bool _isVip;
    private bool _isApplyingLaunchAtStartup;
    private bool _isApplyingWindowsNotifications;
    private bool _isApplyingFocusSound;
    private DispatcherTimer? _ruleMergeToastTimer;
    private bool _isRuleMergeToastVisible;
    private string _ruleMergeToastRange = string.Empty;
    private DispatcherTimer? _ruleLimitToastTimer;
    private bool _isRuleLimitToastVisible;
    private string _ruleLimitToastMessage = string.Empty;
    private readonly Func<DateTime> _clock;

    public SettingsPageViewModel(
        IEnumerable<SettingsToggleItemViewModel> toggleItems,
        IEnumerable<SettingsEntryItemViewModel> entryItems,
        AutomaticRuleModalViewModel? ruleModal = null,
        string dailyLabel = "Daily",
        Func<DateTime>? clock = null,
        ExportRecordsModalViewModel? exportRecordsModal = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        ToggleItems = new ReadOnlyCollection<SettingsToggleItemViewModel>(toggleItems.ToList());
        EntryItems = new ReadOnlyCollection<SettingsEntryItemViewModel>(entryItems.ToList());
        GeneralToggleItems = new ReadOnlyCollection<SettingsToggleItemViewModel>(ToggleItems.Take(5).ToList());
        FloatingWindowItem = ToggleItems.FirstOrDefault(item => item.Key == "FloatingWindow");
        AutomaticBlockingItem = ToggleItems.FirstOrDefault(item => item.Key == "AutomaticBlocking");
        ForcedModeItem = ToggleItems.FirstOrDefault(item => item.Key == "ForcedMode");
        RuleModal = ruleModal ?? AutomaticRuleModalViewModel.CreateDefault();
        RuleActivationModal = new AutomaticRuleActivationModalViewModel();
        ExportRecordsModal = exportRecordsModal ?? new ExportRecordsModalViewModel();
        _dailyLabel = dailyLabel;
        ActivateEntryCommand = new RelayCommand<SettingsEntryItemViewModel>(ActivateEntry);
        OpenRuleModalCommand = new RelayCommand<object>(_ => OpenCreateRule());
        DeleteRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(ToggleRule);
        EditRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(EditRule);
        CloseRuleMergeToastCommand = new RelayCommand<object>(_ => CloseRuleMergeToast());
        CloseRuleLimitToastCommand = new RelayCommand<object>(_ => CloseRuleLimitToast());
        RuleModal.CanSubmitRule = CanSubmitRule;
        RuleModal.ValidateRule = ValidateRule;
        RuleModal.GetRules = () => AutomaticRules.ToArray();
        RuleModal.EditRequested = EditRule;
        RuleModal.MoveRequested = MoveRule;
        RuleModal.ResizeRequested = ResizeRule;
        RuleModal.NewRequested = () => _editingRule = null;
        RuleModal.RuleSubmitted += RuleModal_RuleSubmitted;
        RuleActivationModal.ActivationConfirmed += RuleActivationModal_ActivationConfirmed;
        if (AutomaticBlockingItem is not null)
        {
            AutomaticBlockingItem.PropertyChanged += AutomaticBlockingItem_PropertyChanged;
        }

        if (ForcedModeItem is not null)
        {
            ForcedModeItem.PropertyChanged += ForcedModeItem_PropertyChanged;
            EnsureForcedModeAccessState();
        }

        LaunchAtStartupItem = ToggleItems.FirstOrDefault(item => item.Key == "LaunchAtStartup");
        if (LaunchAtStartupItem is not null)
        {
            LaunchAtStartupItem.PropertyChanged += LaunchAtStartupItem_PropertyChanged;
        }

        WindowsNotificationsItem = ToggleItems.FirstOrDefault(item => item.Key == "WindowsNotifications");
        if (WindowsNotificationsItem is not null)
        {
            WindowsNotificationsItem.PropertyChanged += WindowsNotificationsItem_PropertyChanged;
        }

        FocusSoundItem = ToggleItems.FirstOrDefault(item => item.Key == "FocusSound");
        if (FocusSoundItem is not null)
        {
            FocusSoundItem.PropertyChanged += FocusSoundItem_PropertyChanged;
        }
    }

    private readonly string _dailyLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RulesChanged;

    public event Action<AutomaticRuleItemViewModel>? AutomaticRuleFocusRequested;

    public ReadOnlyCollection<SettingsToggleItemViewModel> ToggleItems { get; }

    public ReadOnlyCollection<SettingsToggleItemViewModel> GeneralToggleItems { get; }

    public ReadOnlyCollection<SettingsEntryItemViewModel> EntryItems { get; }

    public SettingsToggleItemViewModel? AutomaticBlockingItem { get; }

    public SettingsToggleItemViewModel? FloatingWindowItem { get; }

    public bool IsFloatingWindowEnabled => FloatingWindowItem?.IsEnabled == true;

    public bool IsAutomaticBlockingEnabled => AutomaticBlockingItem?.IsEnabled == true;

    public SettingsToggleItemViewModel? ForcedModeItem { get; }

    public SettingsToggleItemViewModel? LaunchAtStartupItem { get; }

    public SettingsToggleItemViewModel? WindowsNotificationsItem { get; }

    public SettingsToggleItemViewModel? FocusSoundItem { get; }

    public event EventHandler<bool>? LaunchAtStartupChanged;
    public event EventHandler<bool>? WindowsNotificationsChanged;
    public event EventHandler<bool>? FocusSoundChanged;

    public bool CanUseForcedMode => ForcedModeAccessPolicy.CanUseForcedMode(_isLoggedIn, _isVip);

    public AutomaticRuleModalViewModel RuleModal { get; }

    public AutomaticRuleActivationModalViewModel RuleActivationModal { get; }

    public ExportRecordsModalViewModel ExportRecordsModal { get; }

    public ObservableCollection<AutomaticRuleItemViewModel> AutomaticRules { get; } = [];

    public bool RequestAutomaticRuleFocus(Guid ruleId)
    {
        var targetRule = AutomaticRules.FirstOrDefault(rule => rule.Id == ruleId);
        foreach (var rule in AutomaticRules)
        {
            rule.SetNavigationHighlight(ReferenceEquals(rule, targetRule));
        }

        if (targetRule is null)
        {
            return false;
        }

        AutomaticRuleFocusRequested?.Invoke(targetRule);
        return true;
    }

    public void ApplyAutomaticRules(IEnumerable<LocalAutomaticRuleDto> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var existing in AutomaticRules) existing.PropertyChanged -= AutomaticRule_PropertyChanged;
        AutomaticRules.Clear();
        foreach (var rule in rules.OrderBy(item => item.SortOrder))
        {
            var dayKeys = rule.ActiveDays.Select(day => day.ToString());
            var repeatText = rule.IsCustom
                ? string.Join(" / ", rule.ActiveDays.OrderBy(GetDaySortOrder).Select(GetDayDisplayName))
                : _dailyLabel;
            var item = new AutomaticRuleItemViewModel(
                rule.Id, repeatText, FormatRuleRange(rule.StartMinutes, rule.EndMinutes), dayKeys,
                rule.StartMinutes, rule.EndMinutes, rule.IsCustom,
                rule.CreatedAtUtc ?? DateTimeOffset.UnixEpoch,
                rule.UpdatedAtUtc ?? DateTimeOffset.UnixEpoch)
            {
                IsEnabled = rule.IsEnabled
            };
            item.SetTarget(rule.TargetId, ResolveTargetName(rule.TargetId));
            item.PropertyChanged += AutomaticRule_PropertyChanged;
            AutomaticRules.Add(item);
        }
        RuleModal.RefreshTimeline();
    }

    public ICommand ActivateEntryCommand { get; }

    public ICommand OpenRuleModalCommand { get; }

    public ICommand DeleteRuleCommand { get; }

    public ICommand ToggleRuleCommand { get; }

    public ICommand EditRuleCommand { get; }

    public ICommand CloseRuleMergeToastCommand { get; }

    public ICommand CloseRuleLimitToastCommand { get; }

    public bool IsRuleMergeToastVisible => _isRuleMergeToastVisible;

    public string RuleMergeToastRange => _ruleMergeToastRange;

    public bool IsRuleLimitToastVisible => _isRuleLimitToastVisible;

    public string RuleLimitToastMessage => _ruleLimitToastMessage;

    public string? LastActivatedEntryKey => _activeEntry?.Key;

    public void SetUserAccess(bool isLoggedIn, bool isVip)
    {
        var couldUseForcedMode = CanUseForcedMode;
        _isLoggedIn = isLoggedIn;
        _isVip = isVip;

        if (couldUseForcedMode != CanUseForcedMode)
        {
            OnPropertyChanged(nameof(CanUseForcedMode));
        }

        EnsureForcedModeAccessState();
    }

    private void OpenCreateRule()
    {
        _editingRule = null;
        RuleModal.OpenTimeline();
    }

    private void EditRule(AutomaticRuleItemViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        _editingRule = rule;
        RuleModal.OpenForEdit(
            rule.IsCustom,
            rule.DayKeys,
            rule.StartMinutes,
            rule.EndMinutes);
        RuleModal.BeginEditor(rule);
    }

    private string? MoveRule(AutomaticRuleItemViewModel original, double start, double end)
        => SaveDraggedRule(original, start, end, preserveDuration: true);

    private string? ResizeRule(AutomaticRuleItemViewModel original, double start, double end)
        => SaveDraggedRule(original, start, end, preserveDuration: false);

    private string? SaveDraggedRule(
        AutomaticRuleItemViewModel original,
        double start,
        double end,
        bool preserveDuration)
    {
        var current = AutomaticRules.FirstOrDefault(rule => rule.Id == original.Id);
        if (current is null || current.StartMinutes != original.StartMinutes || current.EndMinutes != original.EndMinutes)
            return "规则已变化，请重新拖动";
        var duration = end - start;
        if (start < 0 || end > 1440 || duration < RuleTimelineRange.MinimumDurationMinutes)
            return $"时间段不能短于 {RuleTimelineRange.MinimumDurationMinutes:0} 分钟且不能超出当天范围";
        if (duration > AutomaticBlockingDailyLimitValidator.DailyLimitMinutes)
            return "单条规则最长 12 小时";
        if (preserveDuration && duration != current.EndMinutes - current.StartMinutes)
            return "移动必须保持原有时长且不能超出当天范围";
        if (start == current.StartMinutes && end == current.EndMinutes) return null;
        var previous = _editingRule;
        _editingRule = current;
        try
        {
            var draft = new AutomaticRuleDraft(current.IsCustom,
                RuleModal.Weekdays.Where(day => !current.IsCustom || current.DayKeys.Contains(day.Key)).ToArray(),
                FormatRuleTime(start), FormatRuleTime(end), start, end, current.TargetId);
            var error = ValidateRule(draft);
            if (error is not null) return error;
            if (!CanSubmitRule(draft))
                return preserveDuration ? "移动未保存：超过每日屏蔽上限" : "调整未保存：超过每日屏蔽上限";
            RuleModal_RuleSubmitted(this, draft);
            return null;
        }
        finally { _editingRule = previous; }
    }

    private void RuleModal_RuleSubmitted(object? sender, AutomaticRuleDraft rule)
    {
        var repeatText = rule.IsCustom
            ? string.Join(" / ", rule.SelectedDays.Select(day => day.DisplayName))
            : _dailyLabel;

        if (_editingRule is not null)
        {
            _editingRule = AutomaticRules.FirstOrDefault(item => item.Id == _editingRule.Id);
            if (_editingRule is null) return;
            _editingRule.Update(
                repeatText,
                $"{rule.StartTime} – {rule.EndTime}",
                rule.SelectedDays.Select(day => day.Key),
                rule.StartMinutes,
                rule.EndMinutes,
                rule.IsCustom);
            _editingRule.SetTarget(rule.TargetId, ResolveTargetName(rule.TargetId));
            _editingRule = null;
            RulesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var item = new AutomaticRuleItemViewModel(Guid.NewGuid(), repeatText,
            FormatRuleRange(rule.StartMinutes, rule.EndMinutes), rule.SelectedDays.Select(day => day.Key),
            rule.StartMinutes, rule.EndMinutes, rule.IsCustom) { IsEnabled = false };
        item.SetTarget(rule.TargetId, ResolveTargetName(rule.TargetId));
        item.PropertyChanged += AutomaticRule_PropertyChanged;
        AutomaticRules.Add(item);
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool HasSameRepeatScope(
        AutomaticRuleItemViewModel existing,
        AutomaticRuleDraft draft,
        HashSet<string> selectedDays)
        => existing.IsCustom == draft.IsCustom && existing.DayKeys.SetEquals(selectedDays);

    private static bool IntervalsTouch(double firstStart, double firstEnd, double secondStart, double secondEnd)
        => firstStart <= secondEnd && firstEnd >= secondStart;

    private static string FormatRuleRange(double startMinutes, double endMinutes)
        => $"{FormatRuleTime(startMinutes)} – {FormatRuleTime(endMinutes)}";

    private string? ResolveTargetName(string? targetId)
        => string.IsNullOrEmpty(targetId)
            ? null
            : RuleModal.Targets.FirstOrDefault(target => target.Id == targetId)?.Name;

    private static string FormatRuleTime(double minutes)
    {
        var totalMinutes = (int)Math.Round(minutes);
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }

    private void ShowRuleMergeToast(string range)
    {
        _ruleMergeToastRange = range;
        _isRuleMergeToastVisible = true;
        OnPropertyChanged(nameof(RuleMergeToastRange));
        OnPropertyChanged(nameof(IsRuleMergeToastVisible));

        _ruleMergeToastTimer ??= new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _ruleMergeToastTimer.Stop();
        _ruleMergeToastTimer.Tick -= RuleMergeToastTimer_Tick;
        _ruleMergeToastTimer.Tick += RuleMergeToastTimer_Tick;
        _ruleMergeToastTimer.Start();
    }

    private void RuleMergeToastTimer_Tick(object? sender, EventArgs e)
        => CloseRuleMergeToast();

    private void CloseRuleMergeToast()
    {
        _ruleMergeToastTimer?.Stop();
        if (!_isRuleMergeToastVisible)
        {
            return;
        }

        _isRuleMergeToastVisible = false;
        OnPropertyChanged(nameof(IsRuleMergeToastVisible));
    }

    private bool CanSubmitRule(AutomaticRuleDraft draft)
    {
        var candidate = AutomaticBlockingRuleMapper.ToRule(draft, _editingRule?.Id ?? Guid.NewGuid());
        if (!AutomaticBlockingDailyLimitValidator.IsSingleRuleWithinLimit(candidate))
        {
            return false;
        }

        var conflict = AutomaticBlockingDailyLimitValidator.FindConflict(
            AutomaticRules.Select(AutomaticBlockingRuleMapper.ToRule),
            candidate);
        if (conflict is null)
        {
            CloseRuleLimitToast();
            return true;
        }

        CloseRuleMergeToast();
        var dayName = GetDayDisplayName(conflict.Day);
        _ruleLimitToastMessage = conflict.RemainingMinutes == 0
            ? $"{dayName}已达到每日 12 小时上限，无法继续添加"
            : $"{dayName}每日最多屏蔽 12 小时，当前还可添加 {conflict.RemainingMinutes} 分钟";
        _isRuleLimitToastVisible = true;
        OnPropertyChanged(nameof(RuleLimitToastMessage));
        OnPropertyChanged(nameof(IsRuleLimitToastVisible));

        _ruleLimitToastTimer ??= new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _ruleLimitToastTimer.Stop();
        _ruleLimitToastTimer.Tick -= RuleLimitToastTimer_Tick;
        _ruleLimitToastTimer.Tick += RuleLimitToastTimer_Tick;
        _ruleLimitToastTimer.Start();
        return false;
    }

    private void RuleLimitToastTimer_Tick(object? sender, EventArgs e)
        => CloseRuleLimitToast();

    private void CloseRuleLimitToast()
    {
        _ruleLimitToastTimer?.Stop();
        if (!_isRuleLimitToastVisible)
        {
            return;
        }

        _isRuleLimitToastVisible = false;
        OnPropertyChanged(nameof(IsRuleLimitToastVisible));
    }

    private static string GetDayDisplayName(DayOfWeek day)
        => day switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => string.Empty
        };

    private static int GetDaySortOrder(DayOfWeek day)
        => day == DayOfWeek.Sunday ? 6 : (int)day - 1;

    private void DeleteRule(AutomaticRuleItemViewModel? rule)
    {
        rule = AutomaticRules.FirstOrDefault(item => item.Id == rule?.Id);
        if (rule is not null)
        {
            rule.PropertyChanged -= AutomaticRule_PropertyChanged;
            AutomaticRules.Remove(rule);
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AutomaticRule_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutomaticRuleItemViewModel.IsEnabled))
        {
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AutomaticBlockingItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled))
        {
            OnPropertyChanged(nameof(IsAutomaticBlockingEnabled));
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ForcedModeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled))
        {
            EnsureForcedModeAccessState();
        }
    }

    private void LaunchAtStartupItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled) && !_isApplyingLaunchAtStartup)
        {
            LaunchAtStartupChanged?.Invoke(this, LaunchAtStartupItem?.IsEnabled == true);
        }
    }

    private void WindowsNotificationsItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled) && !_isApplyingWindowsNotifications)
        {
            WindowsNotificationsChanged?.Invoke(this, WindowsNotificationsItem?.IsEnabled == true);
        }
    }

    private void FocusSoundItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled) && !_isApplyingFocusSound)
        {
            FocusSoundChanged?.Invoke(this, FocusSoundItem?.IsEnabled == true);
        }
    }

    public void ApplyLaunchAtStartupState(bool enabled)
    {
        if (LaunchAtStartupItem is null)
        {
            return;
        }

        _isApplyingLaunchAtStartup = true;
        try
        {
            LaunchAtStartupItem.IsEnabled = enabled;
        }
        finally
        {
            _isApplyingLaunchAtStartup = false;
        }
    }

    public void ApplyWindowsNotificationsState(bool enabled)
    {
        if (WindowsNotificationsItem is null) return;
        _isApplyingWindowsNotifications = true;
        try { WindowsNotificationsItem.IsEnabled = enabled; }
        finally { _isApplyingWindowsNotifications = false; }
    }

    public void ApplyFocusSoundState(bool enabled)
    {
        if (FocusSoundItem is null) return;
        _isApplyingFocusSound = true;
        try { FocusSoundItem.IsEnabled = enabled; }
        finally { _isApplyingFocusSound = false; }
    }

    private void EnsureForcedModeAccessState()
    {
        if (ForcedModeItem is null)
        {
            return;
        }

        ForcedModeItem.IsVipRestricted = !CanUseForcedMode;
        if (!CanUseForcedMode && ForcedModeItem.IsEnabled)
        {
            ForcedModeItem.IsEnabled = false;
        }
    }

    private void ToggleRule(AutomaticRuleItemViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        if (rule.IsEnabled)
        {
            rule.IsEnabled = false;
            return;
        }

        var now = _clock();
        var willImmediatelyBlock = IsAutomaticBlockingEnabled &&
                                   AutomaticBlockingSchedule.IsWithinSchedule(AutomaticBlockingRuleMapper.ToRule(rule), now) &&
                                   !AutomaticRules.Any(existing =>
                                       !ReferenceEquals(existing, rule) &&
                                       AutomaticBlockingSchedule.IsActive(AutomaticBlockingRuleMapper.ToRule(existing), now));
        if (willImmediatelyBlock)
        {
            RuleActivationModal.Open(rule);
            return;
        }

        EnableRule(rule);
    }

    private void RuleActivationModal_ActivationConfirmed(object? sender, AutomaticRuleItemViewModel rule)
    {
        if (AutomaticRules.Contains(rule) && !rule.IsEnabled)
        {
            EnableRule(rule);
        }
    }

    private void EnableRule(AutomaticRuleItemViewModel rule)
    {
        rule.IsEnabled = true;
    }

    private string? ValidateRule(AutomaticRuleDraft draft)
    {
        if (_editingRule is not null && !AutomaticRules.Any(rule => rule.Id == _editingRule.Id))
            return "该规则已被删除，请关闭编辑窗口后重试";
        if (draft.SelectedDays.Count == 0) return "请至少选择一天";
        var legacyOvernight = _editingRule is not null && _editingRule.EndMinutes < _editingRule.StartMinutes;
        if (draft.StartMinutes < 0 || draft.StartMinutes >= 1440 || draft.EndMinutes < 0 || draft.EndMinutes == draft.StartMinutes
            || (!legacyOvernight && draft.EndMinutes < draft.StartMinutes) || draft.EndMinutes > 1440)
            return "结束时间必须晚于开始时间（00:00–24:00）";
        return AutomaticRules.Any(existing => existing.Id != _editingRule?.Id
            && RuleTimelineRange.Overlaps(draft.StartMinutes, draft.EndMinutes, existing.StartMinutes, existing.EndMinutes))
            ? "该时间段与已有规则重叠，请调整开始或结束时间" : null;
    }

    private void ActivateEntry(SettingsEntryItemViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (_activeEntry is not null)
        {
            _activeEntry.IsActive = false;
        }

        _activeEntry = entry;
        _activeEntry.IsActive = true;
        OnPropertyChanged(nameof(LastActivatedEntryKey));

        if (entry.Key == "ExportRecords")
        {
            ExportRecordsModal.Open();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AutomaticRuleItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private string _repeatText;
    private string _timeRangeText;
    private double _startMinutes;
    private double _endMinutes;
    private bool _isCustom;
    private bool _isNavigationHighlighted;
    private string? _targetName;

    public AutomaticRuleItemViewModel(Guid id, string repeatText, string timeRangeText,
        IEnumerable<string>? dayKeys = null, double startMinutes = 0, double endMinutes = 0, bool isCustom = false, DateTimeOffset? createdAtUtc = null, DateTimeOffset? updatedAtUtc = null)
    {
        Id = id;
        _repeatText = repeatText;
        _timeRangeText = timeRangeText;
        DayKeys = new HashSet<string>(dayKeys ?? [], StringComparer.Ordinal);
        _startMinutes = startMinutes;
        _endMinutes = endMinutes;
        _isCustom = isCustom;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = updatedAtUtc ?? CreatedAtUtc;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string? TargetId { get; set; }

    public string RepeatText => _repeatText;

    public string TimeRangeText => _timeRangeText;

    public string TargetDisplayText => string.IsNullOrWhiteSpace(_targetName) ? "无目标" : _targetName;

    public string ScheduleDisplayText => _isCustom
        ? $"{_timeRangeText} （{_repeatText}）"
        : _timeRangeText;

    public HashSet<string> DayKeys { get; }

    public double StartMinutes => _startMinutes;

    public double EndMinutes => _endMinutes;

    public bool IsCustom => _isCustom;

    public bool IsNavigationHighlighted => _isNavigationHighlighted;

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void SetTarget(string? targetId, string? targetName)
    {
        TargetId = targetId;
        _targetName = string.IsNullOrEmpty(targetId) ? null : targetName;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetDisplayText)));
    }

    public void Update(
        string repeatText,
        string timeRangeText,
        IEnumerable<string> dayKeys,
        double startMinutes,
        double endMinutes,
        bool isCustom)
    {
        _repeatText = repeatText;
        _timeRangeText = timeRangeText;
        DayKeys.Clear();
        DayKeys.UnionWith(dayKeys);
        _startMinutes = startMinutes;
        _endMinutes = endMinutes;
        _isCustom = isCustom;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RepeatText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeRangeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScheduleDisplayText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DayKeys)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartMinutes)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EndMinutes)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCustom)));
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    internal void SetNavigationHighlight(bool isHighlighted)
    {
        if (_isNavigationHighlighted == isHighlighted)
        {
            return;
        }

        _isNavigationHighlighted = isHighlighted;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNavigationHighlighted)));
    }

    public void ConsumeNavigationHighlight() => SetNavigationHighlight(false);
}

public sealed class SettingsToggleItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _isVipRestricted;

    public SettingsToggleItemViewModel(
        string key,
        string title,
        string description,
        string icon,
        bool isEnabled,
        bool hasSeparator = true)
    {
        Key = key;
        Title = title;
        Description = description;
        Icon = icon;
        _isEnabled = isEnabled;
        HasSeparator = hasSeparator;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string Icon { get; }

    public bool HasSeparator { get; }

    public bool IsVipRestricted
    {
        get => _isVipRestricted;
        internal set
        {
            if (_isVipRestricted == value)
            {
                return;
            }

            _isVipRestricted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVipRestricted)));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
}

public sealed class SettingsEntryItemViewModel : INotifyPropertyChanged
{
    private bool _isActive;

    public SettingsEntryItemViewModel(
        string key,
        string title,
        string description,
        string icon,
        bool hasSeparator = true)
    {
        Key = key;
        Title = title;
        Description = description;
        Icon = icon;
        HasSeparator = hasSeparator;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string Icon { get; }

    public bool HasSeparator { get; }

    public bool IsActive
    {
        get => _isActive;
        internal set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}
