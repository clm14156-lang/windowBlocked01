using System.ComponentModel;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed partial class FocusFloatingWindowViewModel
{
    private FocusTaskViewModel? _editParent;
    private FocusSubTaskViewModel? _editChild;
    private string _editTitle = string.Empty;
    private bool _isSubmittingEdit;
    private bool _isTaskMenuOpen;
    public object? EditingItem => (object?)_editChild ?? _editParent;
    public bool IsEditingTask => _editParent is not null;
    public bool HasTaskInteraction => IsCreatingTask || IsEditingTask || _isTaskMenuOpen;
    public string EditTitle
    {
        get => _editTitle;
        set { if (_editTitle == value) return; _editTitle = value ?? string.Empty; OnPropertyChanged(); }
    }
    internal void SetTaskMenuOpen(bool open)
    {
        if (_isTaskMenuOpen == open) return;
        _isTaskMenuOpen = open;
        OnPropertyChanged(nameof(HasTaskInteraction));
    }
    public bool BeginTaskEdit(FocusTaskViewModel parent, FocusSubTaskViewModel? child = null)
    {
        if (_isDisposed || !IsFocusing || parent.IsCompleted || _session.ActiveTarget?.Tasks.Contains(parent) != true ||
            child is not null && !parent.SubTasks.Contains(child)) return false;
        CancelTaskCreation();
        CancelTaskEdit();
        _editParent = parent; _editChild = child;
        if (child is not null) parent.IsSubTasksExpanded = true;
        if (child is not null) parent.SubTasks.CollectionChanged += EditingChildrenChanged;
        EditTitle = child?.Title ?? parent.Name;
        NotifyEditingChanged();
        return true;
    }
    private void EditingChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_isSubmittingEdit && !IsValidEditTarget()) CancelTaskEdit();
    }
    private bool IsValidEditTarget() => !_isDisposed && IsFocusing && _editParent is { IsCompleted: false } parent &&
        _session.ActiveTarget?.Tasks.Contains(parent) == true && (_editChild is null || parent.SubTasks.Contains(_editChild));

    public bool SubmitTaskEdit()
    {
        if (_isSubmittingEdit || !IsValidEditTarget()) return false;
        var title = EditTitle.Trim();
        if (title.Length == 0) return false;
        var parent = _editParent!;
        var child = _editChild;
        if (title == (child?.Title ?? parent.Name)) { CancelTaskEdit(); return true; }
        _isSubmittingEdit = true;
        try
        {
            // Build from the latest shared task so edits in another surface are preserved.
            var now = DateTimeOffset.UtcNow;
            var saved = _session.SaveDrawerTask(new LocalTaskDto(parent.TaskId, parent.TargetId,
                child is null ? title : parent.Name, parent.IsCompleted, _session.ActiveTarget!.Tasks.IndexOf(parent), parent.CreatedAtUtc, now)
            {
                Description = parent.Description, FlagColor = parent.FlagColor, CompletedAtUtc = parent.CompletedAtUtc,
                SubTasks = parent.ExportSubTasks().Select(item => item.Id == child?.Id ? item with { Title = title, UpdatedAtUtc = now } : item).ToArray()
            });
            if (saved) CancelTaskEdit();
            return saved;
        }
        finally { _isSubmittingEdit = false; }
    }
    public void CancelTaskEdit()
    {
        if (_editParent is null) return;
        _editParent.SubTasks.CollectionChanged -= EditingChildrenChanged;
        _editParent = null; _editChild = null;
        EditTitle = string.Empty;
        NotifyEditingChanged();
    }
    private void NotifyEditingChanged()
    {
        OnPropertyChanged(nameof(EditingItem));
        OnPropertyChanged(nameof(IsEditingTask));
        OnPropertyChanged(nameof(HasTaskInteraction));
    }
}
