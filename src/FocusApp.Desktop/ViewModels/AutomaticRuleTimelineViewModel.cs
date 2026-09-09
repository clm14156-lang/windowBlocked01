using System.Collections.ObjectModel;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed partial class AutomaticRuleModalViewModel
{
    private bool _isEditorOpen;
    private string _editorStartText = "09:00", _editorEndText = "12:00";
    private string? _selectedTargetId;
    private AutomaticRuleItemViewModel? _editorRule;
    public Func<IReadOnlyList<AutomaticRuleItemViewModel>> GetRules { get; set; } = () => [];
    public void RefreshTimeline() => OnPropertyChanged(nameof(GetRules));
    public Action? NewRequested { get; set; }
    public Action<AutomaticRuleItemViewModel>? EditRequested { get; set; }
    public Action<AutomaticRuleItemViewModel>? DeleteRequested { get; set; }
    public Func<AutomaticRuleItemViewModel, double, double, string?>? MoveRequested { get; set; }
    public Func<AutomaticRuleItemViewModel, double, double, string?>? ResizeRequested { get; set; }
    public ObservableCollection<RuleTargetOption> Targets { get; } = [];
    public bool IsEditorOpen { get => _isEditorOpen; private set => SetField(ref _isEditorOpen, value); }
    public string EditorStartText { get => _editorStartText; set => SetField(ref _editorStartText, value); }
    public string EditorEndText { get => _editorEndText; set => SetField(ref _editorEndText, value); }
    public string? SelectedTargetId
    {
        get => _selectedTargetId;
        set { if (SetField(ref _selectedTargetId, value)) OnPropertyChanged(nameof(TargetChoice)); }
    }
    public string TargetChoice { get => SelectedTargetId ?? string.Empty; set => SelectedTargetId = string.IsNullOrEmpty(value) ? null : value; }

    public void ApplyTargets(IEnumerable<LocalTargetDto> targets)
    {
        var selected = SelectedTargetId;
        Targets.Clear();
        Targets.Add(new(string.Empty, "-"));
        foreach (var target in targets.Where(target => !target.IsArchived))
            Targets.Add(new(
                target.TargetId,
                target.Name,
                TargetIconCatalog.GetIconSource(target.IconFileName)));
        SelectedTargetId = IsAvailableTarget(selected) ? selected : null;
        OnPropertyChanged(nameof(TargetChoice));
    }

    public void OpenTimeline()
    {
        Open();
        _editorRule = null;
        IsEditorOpen = false;
        SelectedTargetId = null;
    }

    public void BeginEditor(AutomaticRuleItemViewModel? rule, double start = 0, double end = 0)
    {
        _editorRule = rule;
        IsEditing = rule is not null;
        if (rule is null)
        {
            NewRequested?.Invoke();
            IsCustom = false;
            SelectedTargetId = null;
        }
        else SelectedTargetId = IsAvailableTarget(rule.TargetId) ? rule.TargetId : null;
        EditorStartText = FormatTime(rule?.StartMinutes ?? start);
        EditorEndText = FormatTime(rule?.EndMinutes ?? end);
        ValidationMessage = string.Empty;
        IsEditorOpen = true;
    }

    public void CancelEditor() { IsEditorOpen = false; _editorRule = null; }

    public void DeleteEditor()
    {
        var rule = _editorRule;
        CancelEditor();
        if (rule is not null) DeleteRequested?.Invoke(rule);
    }

    public bool SaveEditor()
    {
        var legacyOvernight = _editorRule is not null && _editorRule.EndMinutes < _editorRule.StartMinutes;
        if (!TryParseTime(EditorStartText, out var start) || !TryParseTime(EditorEndText, out var end)
            || start == end || (!legacyOvernight && start > end) || start >= 1440)
        {
            ValidationMessage = "请输入有效时间（HH:mm），结束时间必须晚于开始时间";
            return false;
        }
        if ((end > start ? end - start : 1440 - start + end) > MaximumDurationMinutes)
        {
            ValidationMessage = "单条规则最长 12 小时";
            return false;
        }
        var targetId = IsAvailableTarget(SelectedTargetId) ? SelectedTargetId : null;
        SelectedTargetId = targetId;
        var draft = new AutomaticRuleDraft(IsCustom,
            (IsCustom ? Weekdays.Where(d => d.IsSelected) : Weekdays).ToArray(),
            FormatTime(start), FormatTime(end), start, end, targetId);
        ValidationMessage = ValidateRule?.Invoke(draft) ?? string.Empty;
        if (ValidationMessage.Length > 0) return false;
        if (CanSubmitRule?.Invoke(draft) == false)
        {
            ValidationMessage = string.Empty;
            return false;
        }
        RuleSubmitted?.Invoke(this, draft);
        CancelEditor();
        return true;
    }

    private bool IsAvailableTarget(string? targetId)
        => !string.IsNullOrEmpty(targetId) && Targets.Any(target => target.Id == targetId);

}

public sealed record RuleTargetOption(string? Id, string Name, string? IconSource = null)
{
    public bool HasIcon => !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(IconSource);
}
