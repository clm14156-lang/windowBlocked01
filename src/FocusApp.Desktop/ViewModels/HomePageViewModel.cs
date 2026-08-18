using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomePageViewModel : INotifyPropertyChanged
{
    private readonly HomeDurationOptionViewModel? _customDurationOption;
    private AutomaticRuleItemViewModel? _nextAutomaticRule;
    private DateTime _nextAutomaticStart;
    private bool _automaticBlockingIntervalActive;
    private readonly HashSet<string> _automaticCompletedRuleKeys = [];
    private int _enabledBlockingCount;
    private bool _forcedModeEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public HomePageViewModel(IEnumerable<HomeDurationOptionViewModel> durationOptions)
    {
        DurationOptions = new ReadOnlyCollection<HomeDurationOptionViewModel>(durationOptions.ToList());

        if (DurationOptions.Count == 0)
        {
            throw new ArgumentException("At least one duration option is required.", nameof(durationOptions));
        }

        if (!DurationOptions.Any(option => option.IsSelected))
        {
            DurationOptions[0].IsSelected = true;
        }

        _customDurationOption = DurationOptions.FirstOrDefault(option => !string.IsNullOrEmpty(option.Icon));
        CustomTimeModal = new CustomTimeModalViewModel(ConfirmCustomTime);
        FocusTargetModal = new FocusTargetModalViewModel();
        FocusSession = new FocusSessionViewModel();
        BlockedContentModal = new BlockedContentModalViewModel();
        SelectDurationCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectDuration);
        StartFocusCommand = new RelayCommand<object>(_ => StartFocus());
        OpenFocusTargetCommand = new RelayCommand<object>(_ => FocusTargetModal.Open());
        OpenBlockedContentCommand = new RelayCommand<object>(_ => OpenBlockedContent());
    }

    public ReadOnlyCollection<HomeDurationOptionViewModel> DurationOptions { get; }

    public ICommand SelectDurationCommand { get; }

    public ICommand StartFocusCommand { get; }

    public ICommand OpenFocusTargetCommand { get; }

    public ICommand OpenBlockedContentCommand { get; }

    public CustomTimeModalViewModel CustomTimeModal { get; }

    public FocusTargetModalViewModel FocusTargetModal { get; }

    public FocusSessionViewModel FocusSession { get; }

    public BlockedContentModalViewModel BlockedContentModal { get; }

    public void SetForcedModeEnabled(bool enabled) => _forcedModeEnabled = enabled;

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

    public string NextAutomaticBlockingToolTip => _nextAutomaticRule is null
        ? ""
        : $"{_nextAutomaticStart:HH:mm} 开始自动屏蔽，持续 {FormatRuleDuration(_nextAutomaticRule)}";

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
            return;
        }

        foreach (var rule in rules.Where(item => item.IsEnabled))
        {
            var date = current.Date;
            if (_automaticCompletedRuleKeys.Contains(GetAutomaticRuleKey(rule, date)) ||
                !rule.DayKeys.Contains(GetDayKey(date.DayOfWeek)))
            {
                continue;
            }

            var candidate = date.AddMinutes(rule.StartMinutes);
            if (candidate <= current)
            {
                continue;
            }

            if (_nextAutomaticRule is null || candidate < _nextAutomaticStart)
            {
                _nextAutomaticRule = rule;
                _nextAutomaticStart = candidate;
            }
        }

        OnPropertyChanged(nameof(HasNextAutomaticRule));
        OnPropertyChanged(nameof(NextAutomaticBlockingDisplay));
        OnPropertyChanged(nameof(NextAutomaticStartDisplay));
        OnPropertyChanged(nameof(NextAutomaticBlockingToolTip));
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
        var activeRules = isAutomaticBlockingEnabled
            ? rules.Where(IsActiveRule).ToList()
            : new List<AutomaticRuleItemViewModel>();
        var isActiveWindow = activeRules.Count > 0;

        if (!isActiveWindow)
        {
            _automaticBlockingIntervalActive = false;
            return;
        }

        var newlyExecuted = false;
        foreach (var rule in activeRules)
        {
            newlyExecuted |= _automaticCompletedRuleKeys.Add(GetAutomaticRuleKey(rule, current.Date));
        }

        if (newlyExecuted)
        {
            UpdateAutomaticRules(rules, isAutomaticBlockingEnabled, current);
        }

        if (_automaticBlockingIntervalActive)
        {
            return;
        }

        // Mark the interval before starting so overlapping rules and re-entrant
        // timer ticks cannot launch a second session.
        _automaticBlockingIntervalActive = true;
        if (!FocusSession.IsActive)
        {
            StartFocus();
        }

        bool IsActiveRule(AutomaticRuleItemViewModel rule)
        {
            if (!rule.IsEnabled || !rule.DayKeys.Contains(GetDayKey(current.DayOfWeek)))
            {
                return false;
            }

            var minute = current.TimeOfDay.TotalMinutes;
            return minute >= rule.StartMinutes && minute < rule.EndMinutes;
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
        if (HasBlockingContent) BlockedContentModal.Open();
    }

    private void ConfirmCustomTime(int minutes)
    {
        if (_customDurationOption is null)
        {
            return;
        }

        _customDurationOption.UpdateDuration($"{minutes} 分钟", minutes);
        SelectOnly(_customDurationOption);
    }

    private void StartFocus()
    {
        var selectedDuration = DurationOptions.First(option => option.IsSelected);
        FocusSession.Start(
            selectedDuration.Minutes,
            FocusTargetModal.HasSelectedTarget ? FocusTargetModal.SelectedTarget : null,
            _forcedModeEnabled);
    }

    private void SelectOnly(HomeDurationOptionViewModel option)
    {
        if (option.IsSelected)
        {
            return;
        }

        foreach (var durationOption in DurationOptions)
        {
            durationOption.IsSelected = ReferenceEquals(durationOption, option);
        }
    }

    private static string GetDayKey(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Monday",
        DayOfWeek.Tuesday => "Tuesday",
        DayOfWeek.Wednesday => "Wednesday",
        DayOfWeek.Thursday => "Thursday",
        DayOfWeek.Friday => "Friday",
        DayOfWeek.Saturday => "Saturday",
        _ => "Sunday"
    };

    private static string GetAutomaticRuleKey(AutomaticRuleItemViewModel rule, DateTime date)
        => $"{rule.Id:N}:{date:yyyy-MM-dd}";

    private static string FormatRuleDuration(AutomaticRuleItemViewModel rule)
    {
        var minutes = Math.Max(0, (int)(rule.EndMinutes - rule.StartMinutes));
        return minutes % 60 == 0 ? $"{minutes / 60} 小时" : $"{minutes / 60} 小时 {minutes % 60} 分钟";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
