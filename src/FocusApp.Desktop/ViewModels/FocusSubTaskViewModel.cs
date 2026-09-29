using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusApp.Contracts;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusSubTaskViewModel : INotifyPropertyChanged
{
    private string _title;
    private bool _isCompleted;
    public FocusSubTaskViewModel(LocalSubTaskDto source)
    {
        Id = source.Id;
        TaskId = source.TaskId;
        _title = source.Title;
        _isCompleted = source.IsCompleted;
        CreatedAtUtc = source.CreatedAtUtc;
        UpdatedAtUtc = source.UpdatedAtUtc;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string TaskId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string CompletedTimeDisplay => IsCompleted ? UpdatedAtUtc.ToLocalTime().ToString("HH:mm") : "—";
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompletedTimeDisplay));
        }
    }
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompletedTimeDisplay));
        }
    }
    internal void Apply(LocalSubTaskDto source)
    {
        var titleChanged = _title != source.Title;
        var completionChanged = _isCompleted != source.IsCompleted;
        _title = source.Title;
        _isCompleted = source.IsCompleted;
        UpdatedAtUtc = source.UpdatedAtUtc;
        if (titleChanged) OnPropertyChanged(nameof(Title));
        if (completionChanged) OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CompletedTimeDisplay));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
