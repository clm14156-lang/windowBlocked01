using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

public sealed partial class AutomaticRuleModalViewModel : INotifyPropertyChanged
{
    private const int MaximumDurationMinutes = AutomaticBlockingDailyLimitValidator.DailyLimitMinutes;
    private bool _isOpen, _isEditing, _isCustom;
    private string _validationMessage = string.Empty;
    public AutomaticRuleModalViewModel(IEnumerable<WeekdayOptionViewModel> weekdays)
    {
        Targets.Add(new(string.Empty, "-"));
        Weekdays = new(weekdays.ToList());
        foreach (var day in Weekdays) day.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedDaysText));
        SelectDailyCommand = new RelayCommand<object>(_ => IsCustom = false);
        SelectCustomCommand = new RelayCommand<object>(_ => IsCustom = true);
        CloseCommand = new RelayCommand<object>(_ => Close());
        ConfirmCommand = new RelayCommand<object>(_ => { if (SaveEditor()) Close(); });
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AutomaticRuleDraft>? RuleSubmitted;
    public Func<AutomaticRuleDraft, string?>? ValidateRule { get; set; }
    public Func<AutomaticRuleDraft, bool>? CanSubmitRule { get; set; }
    public ReadOnlyCollection<WeekdayOptionViewModel> Weekdays { get; }
    public ICommand SelectDailyCommand { get; }
    public ICommand SelectCustomCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ConfirmCommand { get; }
    public bool IsOpen { get => _isOpen; private set => SetField(ref _isOpen, value); }
    public bool IsEditing { get => _isEditing; private set => SetField(ref _isEditing, value); }
    public bool IsCustom
    {
        get => _isCustom;
        set { if (SetField(ref _isCustom, value)) OnPropertyChanged(nameof(IsDaily)); }
    }
    public bool IsDaily => !IsCustom;
    public string StartTimeText { get => EditorStartText; set => EditorStartText = value; }
    public string EndTimeText { get => EditorEndText; set => EditorEndText = value; }
    public double StartValue { get => TryParseTime(EditorStartText, out var value) ? value : 0; set => EditorStartText = FormatTime(value); }
    public double EndValue { get => TryParseTime(EditorEndText, out var value) ? value : 0; set => EditorEndText = FormatTime(value); }
    public string SelectedDurationText => $"{(EndValue - StartValue) / 60:0.#}小时";
    public string SelectedDaysText => string.Join("、", Weekdays.Where(d => d.IsSelected).Select(d => d.DisplayName));
    public string ValidationMessage { get => _validationMessage; private set => SetField(ref _validationMessage, value); }
    public void Open()
    {
        SelectedRuleId = null;
        NewRequested?.Invoke();
        IsEditing = false; IsCustom = false; IsEditorOpen = false;
        SelectedTargetId = null;
        ValidationMessage = string.Empty;
        EditorStartText = "09:00"; EditorEndText = "12:00";
        for (var index = 0; index < Weekdays.Count; index++) Weekdays[index].IsSelected = index is 0 or 2 or 4;
        IsOpen = true;
    }
    public void OpenForEdit(bool isCustom, IEnumerable<string> selectedDayKeys, double startMinutes, double endMinutes)
    {
        IsEditing = true; IsCustom = isCustom;
        var selected = selectedDayKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var day in Weekdays) day.IsSelected = selected.Contains(day.Key);
        EditorStartText = FormatTime(startMinutes); EditorEndText = FormatTime(endMinutes);
        ValidationMessage = string.Empty;
        IsOpen = true;
    }
    private void Close() { CancelEditor(); SelectedRuleId = null; IsOpen = false; }
    private static bool TryParseTime(string value, out double minutes)
    {
        minutes = 0;
        var parts = value.Trim().Split(':');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 2 || parts[1].Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutePart) ||
            hours is < 0 or > 24 || minutePart is < 0 or > 59 || (hours == 24 && minutePart != 0)) return false;
        minutes = hours * 60 + minutePart;
        return true;
    }
    private static string FormatTime(double minutes) => $"{(int)Math.Round(minutes) / 60:00}:{(int)Math.Round(minutes) % 60:00}";
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public static AutomaticRuleModalViewModel CreateDefault() => new(
        new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select((day, index) => new WeekdayOptionViewModel(day, "周" + "一二三四五六日"[index], (index + 1).ToString(), false)));
}

public sealed class WeekdayOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public WeekdayOptionViewModel(string key, string displayName, string shortName, bool isSelected)
    {
        Key = key;
        DisplayName = displayName;
        ShortName = shortName;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string DisplayName { get; }

    public string ChineseName => Key switch { "Monday" => "一", "Tuesday" => "二", "Wednesday" => "三", "Thursday" => "四", "Friday" => "五", "Saturday" => "六", _ => "日" };

    public string ShortName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record AutomaticRuleDraft(
    bool IsCustom,
    IReadOnlyList<WeekdayOptionViewModel> SelectedDays,
    string StartTime,
    string EndTime,
    double StartMinutes,
    double EndMinutes,
    string? TargetId = null);
