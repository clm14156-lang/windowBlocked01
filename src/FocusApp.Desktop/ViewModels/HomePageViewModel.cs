using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed class HomePageViewModel : INotifyPropertyChanged
{
    private readonly HomeDurationOptionViewModel _customDurationOption;
    private readonly List<HomeDurationOptionViewModel> _displayOrder = [];
    private HomeDurationOptionViewModel _currentDurationOption;
    private AutomaticRuleItemViewModel? _nextAutomaticRule;
    private DateTime _nextAutomaticStart;
    private readonly AutomaticBlockingScheduler _automaticBlockingScheduler;
    private int _enabledBlockingCount;
    private bool _isLoggedIn;
    private bool _isVip;
    private bool _isForcedModeRequested;
    private FocusStartModeDecision _lastFocusStartDecision = FocusStartModeDecision.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;

    public HomePageViewModel(
        IEnumerable<HomeDurationOptionViewModel> durationOptions,
        AutomaticBlockingScheduler? automaticBlockingScheduler = null,
        FocusSessionViewModel? focusSession = null)
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

        _currentDurationOption = DurationOptions.First(option => option.IsSelected && !ReferenceEquals(option, _customDurationOption));
        _currentDurationOption.IsCurrent = true;
        _displayOrder.AddRange(DurationOptions.Where(option => option.IsSelected && !ReferenceEquals(option, _customDurationOption)));

        foreach (var option in DurationOptions)
        {
            option.PropertyChanged += DurationOption_PropertyChanged;
        }
        RefreshVisibleDurationOptions();

        CustomTimeModal = new CustomTimeModalViewModel(ConfirmCustomTime, commonOptions, ToggleCommonTimeVisibility, DeleteCommonTime);
        FocusTargetModal = new FocusTargetModalViewModel();
        FocusSession = focusSession ?? new FocusSessionViewModel();
        FocusSession.SetForcedModeStartPermission(() =>
            ForcedModeAccessPolicy.EvaluateStart(_isLoggedIn, _isVip, _isForcedModeRequested) ==
            FocusStartModeDecision.Forced);
        BlockedContentModal = new BlockedContentModalViewModel();
        SelectDurationCommand = new RelayCommand<HomeDurationOptionViewModel>(SelectDuration);
        StartFocusCommand = new RelayCommand<object>(_ => StartFocus());
        OpenFocusTargetCommand = new RelayCommand<object>(_ => FocusTargetModal.Open());
        OpenBlockedContentCommand = new RelayCommand<object>(_ => OpenBlockedContent());
    }

    public ObservableCollection<HomeDurationOptionViewModel> DurationOptions { get; }

    public ObservableCollection<HomeDurationOptionViewModel> VisibleDurationOptions { get; }

    public HomeDurationOptionViewModel CurrentDurationOption => _currentDurationOption;

    public ICommand SelectDurationCommand { get; }

    public ICommand StartFocusCommand { get; }

    public ICommand OpenFocusTargetCommand { get; }

    public ICommand OpenBlockedContentCommand { get; }

    public CustomTimeModalViewModel CustomTimeModal { get; }

    public FocusTargetModalViewModel FocusTargetModal { get; }

    public FocusSessionViewModel FocusSession { get; }

    public BlockedContentModalViewModel BlockedContentModal { get; }

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
            StartFocus(evaluation.StartRequest.FocusMinutes);
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

    private bool ConfirmCustomTime(int minutes)
    {
        if (minutes <= 0 || DurationOptions.Count(item => !ReferenceEquals(item, _customDurationOption)) >= 9 ||
            DurationOptions.Any(option => option.Minutes == minutes && !ReferenceEquals(option, _customDurationOption)))
        {
            return false;
        }

        var option = new HomeDurationOptionViewModel($"{minutes} 分钟", string.Empty, false, minutes);
        DurationOptions.Insert(Math.Max(0, DurationOptions.Count - 1), option);
        option.PropertyChanged += DurationOption_PropertyChanged;
        CustomTimeModal.CommonTimes.Add(option);
        option.IsSelected = DurationOptions.Count(item => item.IsSelected) < 4;
        if (option.IsSelected)
        {
            _displayOrder.Add(option);
        }
        OnPropertyChanged(nameof(DurationOptions));
        RefreshVisibleDurationOptions();
        return true;
    }

    private void StartFocus(int? minutes = null)
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
        FocusSession.Start(
            minutes ?? selectedDuration.Minutes,
            FocusTargetModal.HasSelectedTarget ? FocusTargetModal.SelectedTarget : null,
            decision == FocusStartModeDecision.Forced);
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
    }

    private void ToggleCommonTimeVisibility(int minutes)
    {
        var option = DurationOptions.FirstOrDefault(item => item.Minutes == minutes && !ReferenceEquals(item, _customDurationOption));
        if (option is null) return;
        if (option.IsSelected)
        {
            option.IsSelected = false;
            _displayOrder.Remove(option);
            RefreshVisibleDurationOptions();
            return;
        }

        if (_displayOrder.Count >= 4)
        {
            var oldest = _displayOrder[0];
            _displayOrder.RemoveAt(0);
            oldest.IsSelected = false;
        }

        option.IsSelected = true;
        _displayOrder.Add(option);
        SelectOnly(option);
        RefreshVisibleDurationOptions();
    }

    private void DeleteCommonTime(int minutes)
    {
        var option = DurationOptions.FirstOrDefault(item => item.Minutes == minutes && !ReferenceEquals(item, _customDurationOption));
        if (option is null) return;
        DurationOptions.Remove(option);
        _displayOrder.Remove(option);
        option.PropertyChanged -= DurationOption_PropertyChanged;
        OnPropertyChanged(nameof(DurationOptions));
        RefreshVisibleDurationOptions();
    }

    private void DurationOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeDurationOptionViewModel.IsSelected))
        {
            RefreshVisibleDurationOptions();
        }
    }

    private void RefreshVisibleDurationOptions()
    {
        VisibleDurationOptions.Clear();
        foreach (var option in _displayOrder.Where(item => item.IsSelected))
        {
            VisibleDurationOptions.Add(option);
        }
        VisibleDurationOptions.Add(_customDurationOption);
        OnPropertyChanged(nameof(VisibleDurationOptions));
    }

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
