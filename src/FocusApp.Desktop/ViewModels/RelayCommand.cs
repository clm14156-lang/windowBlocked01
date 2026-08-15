using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

internal sealed class RelayCommand<T> : ICommand
    where T : class
{
    private readonly Action<T?> _execute;

    public RelayCommand(Action<T?> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is null or T;

    public void Execute(object? parameter) => _execute(parameter as T);
}
