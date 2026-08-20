using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class SettingsPageViewModel : INotifyPropertyChanged
{
    private SettingsEntryItemViewModel? _activeEntry;

    public SettingsPageViewModel(
        IEnumerable<SettingsToggleItemViewModel> toggleItems,
        IEnumerable<SettingsEntryItemViewModel> entryItems,
        AutomaticRuleModalViewModel? ruleModal = null,
        string dailyLabel = "Daily")
    {
        ToggleItems = new ReadOnlyCollection<SettingsToggleItemViewModel>(toggleItems.ToList());
        EntryItems = new ReadOnlyCollection<SettingsEntryItemViewModel>(entryItems.ToList());
        GeneralToggleItems = new ReadOnlyCollection<SettingsToggleItemViewModel>(ToggleItems.Take(5).ToList());
        FloatingWindowItem = ToggleItems.FirstOrDefault(item => item.Key == "FloatingWindow");
        AutomaticBlockingItem = ToggleItems.FirstOrDefault(item => item.Key == "AutomaticBlocking");
        ForcedModeItem = ToggleItems.FirstOrDefault(item => item.Key == "ForcedMode");
        RuleModal = ruleModal ?? AutomaticRuleModalViewModel.CreateDefault();
        _dailyLabel = dailyLabel;
        ActivateEntryCommand = new RelayCommand<SettingsEntryItemViewModel>(ActivateEntry);
        OpenRuleModalCommand = new RelayCommand<object>(_ => RuleModal.Open());
        DeleteRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(ToggleRule);
        EditRuleCommand = new RelayCommand<AutomaticRuleItemViewModel>(_ => { });
        RuleModal.ValidateRule = ValidateRule;
        RuleModal.RuleCreated += RuleModal_RuleCreated;
        if (AutomaticBlockingItem is not null)
        {
            AutomaticBlockingItem.PropertyChanged += AutomaticBlockingItem_PropertyChanged;
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

    public AutomaticRuleModalViewModel RuleModal { get; }

    public ObservableCollection<AutomaticRuleItemViewModel> AutomaticRules { get; } = [];

    public ICommand ActivateEntryCommand { get; }

    public ICommand OpenRuleModalCommand { get; }

    public ICommand DeleteRuleCommand { get; }

    public ICommand ToggleRuleCommand { get; }

    public ICommand EditRuleCommand { get; }

    public string? LastActivatedEntryKey => _activeEntry?.Key;

    private void RuleModal_RuleCreated(object? sender, AutomaticRuleDraft rule)
    {
        var repeatText = rule.IsCustom
            ? string.Join(" / ", rule.SelectedDays.Select(day => day.DisplayName))
            : _dailyLabel;

        var item = new AutomaticRuleItemViewModel(
            Guid.NewGuid(),
            repeatText,
            $"{rule.StartTime} – {rule.EndTime}",
            rule.SelectedDays.Select(day => day.Key),
            rule.StartMinutes,
            rule.EndMinutes);
        item.PropertyChanged += AutomaticRule_PropertyChanged;
        AutomaticRules.Add(item);
        RulesChanged?.Invoke(this, EventArgs.Empty);
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

    private void ToggleRule(AutomaticRuleItemViewModel? rule)
    {
        if (rule is not null)
        {
            rule.IsEnabled = !rule.IsEnabled;
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? ValidateRule(AutomaticRuleDraft draft)
    {
        var newDays = draft.SelectedDays.Select(day => day.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var existing in AutomaticRules)
        {
            if (!existing.IsEnabled || !existing.DayKeys.Any(newDays.Contains))
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
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AutomaticRuleItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    public AutomaticRuleItemViewModel(Guid id, string repeatText, string timeRangeText,
        IEnumerable<string>? dayKeys = null, double startMinutes = 0, double endMinutes = 0)
    {
        Id = id;
        RepeatText = repeatText;
        TimeRangeText = timeRangeText;
        DayKeys = new HashSet<string>(dayKeys ?? [], StringComparer.Ordinal);
        StartMinutes = startMinutes;
        EndMinutes = endMinutes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string RepeatText { get; }

    public string TimeRangeText { get; }

    public HashSet<string> DayKeys { get; }

    public double StartMinutes { get; }

    public double EndMinutes { get; }

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
