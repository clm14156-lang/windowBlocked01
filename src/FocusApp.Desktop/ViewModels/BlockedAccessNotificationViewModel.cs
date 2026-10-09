using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class BlockedAccessNotificationViewModel
{
    public BlockedAccessNotificationViewModel(
        string name,
        string address,
        string type,
        string targetKindDisplay,
        Action? suppressForSession = null)
    {
        Name = name;
        Address = address;
        Type = type;
        TargetKindDisplay = targetKindDisplay;
        CloseCommand = new RelayCommand<object>(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
        SuppressForSessionCommand = new RelayCommand<object>(_ =>
        {
            if (!IsWebsite || suppressForSession is null) return;
            suppressForSession();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }, _ => IsWebsite && suppressForSession is not null);
    }

    public event EventHandler? CloseRequested;

    public string Name { get; }

    public string Address { get; }

    public string Type { get; }

    public string TargetKindDisplay { get; }

    public bool IsWebsite => string.Equals(Type, "Website", StringComparison.OrdinalIgnoreCase);

    public ICommand CloseCommand { get; }
    public ICommand SuppressForSessionCommand { get; }
}
