using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
