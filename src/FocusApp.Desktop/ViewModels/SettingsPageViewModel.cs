using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusApp.Desktop.ViewModels;

public sealed class SettingsPageViewModel : INotifyPropertyChanged
{
    private SettingsEntryItemViewModel? _activeEntry;
    private AutomaticRuleItemViewModel? _editingRule;
    private bool _isLoggedIn;
    private bool _isVip;
    private DispatcherTimer? _ruleMergeToastTimer;
    private bool _isRuleMergeToastVisible;
    private string _ruleMergeToastRange = string.Empty;
    private readonly Func<DateTime> _clock;

    public SettingsPageViewModel(
        IEnumerable<SettingsToggleItemViewModel> toggleItems,
        IEnumerable<SettingsEntryItemViewModel> entryItems,
        AutomaticRuleModalViewModel? ruleModal = null,
        string dailyLabel = "Daily",
        Func<DateTime>? clock = null)
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
        ExportRecordsModal = new ExportRecordsModalViewModel();
        _dailyLabel = dailyLabel;
        ActivateEntryCommand = new RelayCommand<SettingsEntryItemViewModel>(ActivateEntry);
        OpenRuleModalCommand = new RelayCommand<object>(_ => OpenCreateRule());
        DeleteRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(ToggleRule);
        EditRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(EditRule);
        CloseRuleMergeToastCommand = new RelayCommand<object>(_ => CloseRuleMergeToast());
        RuleModal.ValidateRule = ValidateRule;
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
    }

    private readonly string _dailyLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RulesChanged;

    public ReadOnlyCollection<SettingsToggleItemViewModel> ToggleItems { get; }

    public ReadOnlyCollection<SettingsToggleItemViewModel> GeneralToggleItems { get; }

    public ReadOnlyCollection<SettingsEntryItemViewModel> EntryItems { get; }

    public SettingsToggleItemViewModel? AutomaticBlockingItem { get; }

    public SettingsToggleItemViewModel? FloatingWindowItem { get; }

    public bool IsFloatingWindowEnabled => FloatingWindowItem?.IsEnabled == true;

    public bool IsAutomaticBlockingEnabled => AutomaticBlockingItem?.IsEnabled == true;

    public SettingsToggleItemViewModel? ForcedModeItem { get; }

    public bool CanUseForcedMode => _isLoggedIn && _isVip;

    public AutomaticRuleModalViewModel RuleModal { get; }

    public AutomaticRuleActivationModalViewModel RuleActivationModal { get; }

    public ExportRecordsModalViewModel ExportRecordsModal { get; }

    public ObservableCollection<AutomaticRuleItemViewModel> AutomaticRules { get; } = [];

    public ICommand ActivateEntryCommand { get; }

    public ICommand OpenRuleModalCommand { get; }

    public ICommand DeleteRuleCommand { get; }

    public ICommand ToggleRuleCommand { get; }

    public ICommand EditRuleCommand { get; }

    public ICommand CloseRuleMergeToastCommand { get; }

    public bool IsRuleMergeToastVisible => _isRuleMergeToastVisible;

    public string RuleMergeToastRange => _ruleMergeToastRange;

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
        RuleModal.Open();
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
    }

    private void RuleModal_RuleSubmitted(object? sender, AutomaticRuleDraft rule)
    {
        var repeatText = rule.IsCustom
            ? string.Join(" / ", rule.SelectedDays.Select(day => day.DisplayName))
            : _dailyLabel;

        if (_editingRule is not null)
        {
            _editingRule.Update(
                repeatText,
                $"{rule.StartTime} – {rule.EndTime}",
                rule.SelectedDays.Select(day => day.Key),
                rule.StartMinutes,
                rule.EndMinutes,
                rule.IsCustom);
            _editingRule = null;
            RulesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        AddOrMergeRule(rule, repeatText);
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddOrMergeRule(AutomaticRuleDraft draft, string repeatText)
    {
        var selectedDays = draft.SelectedDays.Select(day => day.Key).ToHashSet(StringComparer.Ordinal);
        var mergedStart = draft.StartMinutes;
        var mergedEnd = draft.EndMinutes;
        var matches = new List<AutomaticRuleItemViewModel>();

        // Expand the candidate range until every connected rule in the same
        // recurrence scope has been included. This handles one new interval joining
        // several existing intervals in a single operation.
        while (true)
        {
            var newlyConnected = AutomaticRules
                .Where(existing => !matches.Contains(existing)
                    && HasSameRepeatScope(existing, draft, selectedDays)
                    && IntervalsTouch(existing.StartMinutes, existing.EndMinutes, mergedStart, mergedEnd))
                .ToList();

            if (newlyConnected.Count == 0)
            {
                break;
            }

            matches.AddRange(newlyConnected);
            mergedStart = Math.Min(mergedStart, newlyConnected.Min(item => item.StartMinutes));
            mergedEnd = Math.Max(mergedEnd, newlyConnected.Max(item => item.EndMinutes));
        }

        if (matches.Count == 0)
        {
            CloseRuleMergeToast();
            var item = new AutomaticRuleItemViewModel(
                Guid.NewGuid(),
                repeatText,
                FormatRuleRange(mergedStart, mergedEnd),
                selectedDays,
                mergedStart,
                mergedEnd,
                draft.IsCustom);
            item.IsEnabled = false;
            item.PropertyChanged += AutomaticRule_PropertyChanged;
            AutomaticRules.Add(item);
            return;
        }

        var keeper = matches[0];
        foreach (var redundant in matches.Skip(1))
        {
            redundant.PropertyChanged -= AutomaticRule_PropertyChanged;
            AutomaticRules.Remove(redundant);
        }

        keeper.Update(
            repeatText,
            FormatRuleRange(mergedStart, mergedEnd),
            selectedDays,
            mergedStart,
            mergedEnd,
            draft.IsCustom);
        ShowRuleMergeToast(FormatRuleRange(mergedStart, mergedEnd));
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

    private void DeleteRule(AutomaticRuleItemViewModel? rule)
    {
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
            RulesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var now = _clock();
        var willImmediatelyBlock = IsAutomaticBlockingEnabled &&
                                   AutomaticRuleSchedule.IsWithinSchedule(rule, now) &&
                                   !AutomaticRules.Any(existing =>
                                       !ReferenceEquals(existing, rule) &&
                                       AutomaticRuleSchedule.IsActive(existing, now));
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
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? ValidateRule(AutomaticRuleDraft draft)
    {
        // New rules are normalized by AddOrMergeRule. Keep validation for edits,
        // where the existing conflict feedback and in-place update are expected.
        if (_editingRule is null)
        {
            return null;
        }

        var newDays = draft.SelectedDays.Select(day => day.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var existing in AutomaticRules)
        {
            if (ReferenceEquals(existing, _editingRule))
            {
                continue;
            }

            if (!existing.DayKeys.Any(newDays.Contains))
            {
                continue;
            }

            var sameDays = existing.DayKeys.SetEquals(newDays);
            if (sameDays && existing.StartMinutes == draft.StartMinutes && existing.EndMinutes == draft.EndMinutes)
            {
                return "已存在相同的自动屏蔽规则";
            }

            if (existing.StartMinutes <= draft.StartMinutes && existing.EndMinutes >= draft.EndMinutes &&
                existing.DayKeys.IsSupersetOf(newDays))
            {
                return "该时间段已被现有规则覆盖";
            }
        }

        return null;
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

    public AutomaticRuleItemViewModel(Guid id, string repeatText, string timeRangeText,
        IEnumerable<string>? dayKeys = null, double startMinutes = 0, double endMinutes = 0, bool isCustom = false)
    {
        Id = id;
        _repeatText = repeatText;
        _timeRangeText = timeRangeText;
        DayKeys = new HashSet<string>(dayKeys ?? [], StringComparer.Ordinal);
        _startMinutes = startMinutes;
        _endMinutes = endMinutes;
        _isCustom = isCustom;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string RepeatText => _repeatText;

    public string TimeRangeText => _timeRangeText;

    public HashSet<string> DayKeys { get; }

    public double StartMinutes => _startMinutes;

    public double EndMinutes => _endMinutes;

    public bool IsCustom => _isCustom;

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RepeatText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeRangeText)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
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
