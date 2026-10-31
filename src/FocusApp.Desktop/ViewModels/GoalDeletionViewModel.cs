using System.ComponentModel;
using System.Windows.Input;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class GoalDeletionViewModel(Func<GoalOverviewItemViewModel, Task> deleteGoal) : INotifyPropertyChanged
{
    private GoalOverviewItemViewModel? _goal;
    private RelayCommand<object>? _confirmCommand;
    private RelayCommand<object>? _cancelCommand;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen { get; private set; }
    public bool IsDeleting { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public bool HasError => ErrorMessage.Length > 0;
    public string ConfirmButtonText => IsDeleting ? "删除中…" : "删除目标";
    public ICommand ConfirmCommand => _confirmCommand ??= new RelayCommand<object>(_ => _ = ConfirmAsync(), _ => IsOpen && !IsDeleting);
    public ICommand CancelCommand => _cancelCommand ??= new RelayCommand<object>(_ => Close(), _ => !IsDeleting);

    internal void Open(GoalOverviewItemViewModel goal)
    {
        if (IsDeleting) return;
        _goal = goal;
        ErrorMessage = string.Empty;
        IsOpen = true;
        Notify();
    }

    public void Close()
    {
        if (IsDeleting) return;
        _goal = null;
        IsOpen = false;
        ErrorMessage = string.Empty;
        Notify();
    }

    public async Task<bool> ConfirmAsync()
    {
        if (!IsOpen || IsDeleting || _goal is not { } goal) return false;
        IsDeleting = true;
        ErrorMessage = string.Empty;
        Notify();
        try
        {
            await deleteGoal(goal);
            _goal = null;
            IsOpen = false;
            return true;
        }
        catch (Exception error)
        {
            ErrorMessage = error is InvalidOperationException or IpcConnectionException ||
                error is IpcRemoteException { Error.Code: IpcErrorCode.BusinessRejected }
                ? error.Message : "删除失败，请稍后重试。";
            DesktopDiagnostics.RecordException("DeleteArchivedGoal", error);
            return false;
        }
        finally { IsDeleting = false; Notify(); }
    }

    private void Notify()
    {
        foreach (var property in new[] { nameof(IsOpen), nameof(IsDeleting), nameof(ErrorMessage), nameof(HasError), nameof(ConfirmButtonText) })
            PropertyChanged?.Invoke(this, new(property));
        _confirmCommand?.NotifyCanExecuteChanged();
        _cancelCommand?.NotifyCanExecuteChanged();
    }
}
