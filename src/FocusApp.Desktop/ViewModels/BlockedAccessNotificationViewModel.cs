using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class BlockedAccessNotificationViewModel
{
    public BlockedAccessNotificationViewModel(
        string name,
        string address,
        string type,
        string targetKindDisplay)
    {
        Name = name;
        Address = address;
        Type = type;
        TargetKindDisplay = targetKindDisplay;
        CloseCommand = new RelayCommand<object>(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CloseRequested;

    public string Name { get; }

    public string Address { get; }

    public string Type { get; }

    public string TargetKindDisplay { get; }

    public ICommand CloseCommand { get; }
}
