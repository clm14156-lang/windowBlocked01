using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        running = true;
        NotifyCanExecuteChanged();
        try { await execute(); }
        finally { running = false; NotifyCanExecuteChanged(); }
    }
}
