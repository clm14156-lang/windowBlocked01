using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.Services;

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
        _flagColor = TaskFlagColors.Normalize(source.FlagColor);
        CreatedAtUtc = source.CreatedAtUtc;
        UpdatedAtUtc = source.UpdatedAtUtc;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; }
    public string TaskId { get; }
    private string? _flagColor;
    public string? FlagColor
    {
        get => _flagColor;
        set
        {
            var color = TaskFlagColors.Normalize(value);
            if (_flagColor == color) return;
            _flagColor = color;
            OnPropertyChanged(); OnPropertyChanged(nameof(HasFlag)); OnPropertyChanged(nameof(FlagBrush));
            OnPropertyChanged(nameof(MarkerIcon)); OnPropertyChanged(nameof(MarkerLabel));
        }
    }
    public bool HasFlag => FlagColor is not null;
    public System.Windows.Media.Brush FlagBrush => FocusTaskFlags.BrushFor(FlagColor);
    public System.Windows.Media.ImageSource? MarkerIcon => TaskMarkerCatalog.Find(FlagColor)?.Icon;
    public string? MarkerLabel => TaskMarkerCatalog.Find(FlagColor)?.Label;
    private bool _isLastSibling;
    public bool IsLastSibling
    {
        get => _isLastSibling;
        internal set
        {
            if (_isLastSibling == value) return;
            _isLastSibling = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNextSibling));
        }
    }
    public bool HasNextSibling => !IsLastSibling;

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
        var flagChanged = _flagColor != TaskFlagColors.Normalize(source.FlagColor);
        _title = source.Title;
        _isCompleted = source.IsCompleted;
        _flagColor = TaskFlagColors.Normalize(source.FlagColor);
        UpdatedAtUtc = source.UpdatedAtUtc;
        if (titleChanged) OnPropertyChanged(nameof(Title));
        if (completionChanged) OnPropertyChanged(nameof(IsCompleted));
        if (flagChanged)
        {
            OnPropertyChanged(nameof(FlagColor)); OnPropertyChanged(nameof(HasFlag)); OnPropertyChanged(nameof(FlagBrush));
            OnPropertyChanged(nameof(MarkerIcon)); OnPropertyChanged(nameof(MarkerLabel));
        }
        OnPropertyChanged(nameof(CompletedTimeDisplay));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
