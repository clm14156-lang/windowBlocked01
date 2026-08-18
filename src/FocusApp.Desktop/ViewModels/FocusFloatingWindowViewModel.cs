using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

/// <summary>
/// Presentation-only adapter for the focus floating window. The session remains
/// the single source of truth for time and task state.
/// </summary>
public sealed class FocusFloatingWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly FocusSessionViewModel _session;
    private readonly HashSet<FocusTaskViewModel> _subscribedTasks = [];
    private bool _isDisposed;

    public FocusFloatingWindowViewModel(FocusSessionViewModel session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.PropertyChanged += Session_PropertyChanged;
        _session.PendingTasks.CollectionChanged += PendingTasks_CollectionChanged;
        SubscribeTasks();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RemainingTimeDisplay => _session.RemainingTimeDisplay;

    public string VerticalRemainingDisplay => _session.RemainingFocusSeconds >= 60
        ? (_session.RemainingFocusSeconds / 60).ToString()
        : _session.RemainingFocusSeconds.ToString();

    public double RemainingProgress => _session.RemainingProgress;

    public bool IsFocusing => _session.IsFocusing;

    public FocusTaskViewModel? CurrentTask => _session.PendingTasks.FirstOrDefault();

    public bool HasCurrentTask => CurrentTask is not null;

    public string CurrentTaskName => CurrentTask?.Name ?? string.Empty;

    public ICommand ToggleTaskCompletedCommand => _session.ToggleTaskCompletedCommand;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _session.PropertyChanged -= Session_PropertyChanged;
        _session.PendingTasks.CollectionChanged -= PendingTasks_CollectionChanged;
        UnsubscribeTasks();
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionViewModel.RemainingTimeDisplay)
            or nameof(FocusSessionViewModel.RemainingProgress)
            or nameof(FocusSessionViewModel.IsFocusing))
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(FocusSessionViewModel.RemainingTimeDisplay))
            {
                OnPropertyChanged(nameof(VerticalRemainingDisplay));
            }
        }
    }

    private void PendingTasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeTasks();
        SubscribeTasks();
        OnPropertyChanged(nameof(CurrentTask));
        OnPropertyChanged(nameof(HasCurrentTask));
        OnPropertyChanged(nameof(CurrentTaskName));
    }

    private void SubscribeTasks()
    {
        foreach (var task in _session.PendingTasks)
        {
            if (_subscribedTasks.Add(task))
            {
                task.PropertyChanged += Task_PropertyChanged;
            }
        }
    }

    private void UnsubscribeTasks()
    {
        foreach (var task in _subscribedTasks)
        {
            task.PropertyChanged -= Task_PropertyChanged;
        }

        _subscribedTasks.Clear();
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusTaskViewModel.Name))
        {
            OnPropertyChanged(nameof(CurrentTaskName));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
