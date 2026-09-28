using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalTaskInlineEditorViewModel : INotifyPropertyChanged
{
    private string? _field;
    private string _value = string.Empty;
    private bool _isSaving;
    private bool _isPendingCreation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsEditingName => _field == "name";
    public bool IsEditingRemark => _field == "remark";
    public bool IsAddingSubTask => _field == "subtask";
    public bool IsActive => _field is not null;
    public string Value { get => _value; set { if (_value == value) return; _value = value; Notify(); } }
    public bool IsSaving { get => _isSaving; internal set { if (_isSaving == value) return; _isSaving = value; Notify(); } }
    public bool IsPendingCreation
    {
        get => _isPendingCreation;
        internal set { if (_isPendingCreation == value) return; _isPendingCreation = value; Notify(); }
    }
    internal void Begin(string field, string value)
    {
        _field = field;
        Value = value;
        NotifyState();
    }
    internal void Cancel()
    {
        _field = null;
        Value = string.Empty;
        NotifyState();
    }
    private void NotifyState()
    {
        Notify(nameof(IsEditingName)); Notify(nameof(IsEditingRemark));
        Notify(nameof(IsAddingSubTask)); Notify(nameof(IsActive));
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
