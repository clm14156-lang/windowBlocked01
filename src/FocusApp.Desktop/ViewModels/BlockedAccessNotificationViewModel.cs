using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Core;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class BlockedAccessNotificationViewModel : INotifyPropertyChanged
{
    private ImageSource? _favicon;

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
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Address { get; }

    public string Type { get; }

    public string TargetKindDisplay { get; }

    public bool IsWebsite => string.Equals(Type, "Website", StringComparison.OrdinalIgnoreCase);

    public ImageSource? Favicon => _favicon;

    public ICommand CloseCommand { get; }
    public ICommand SuppressForSessionCommand { get; }

    internal async Task LoadFaviconAsync(
        IFaviconService faviconService,
        IEnumerable<BlockingWebsiteItemViewModel> websites)
    {
        if (!IsWebsite) return;

        // Match only to select presentation assets; access decisions remain unchanged.
        var host = AccessControlService.NormalizeWebsiteHost(Address);
        if (host is null) return;
        var website = websites
            .Select(item => new { Item = item, Host = AccessControlService.NormalizeWebsiteHost(item.Address) })
            .Where(item => item.Host is not null &&
                (string.Equals(host, item.Host, StringComparison.OrdinalIgnoreCase) ||
                 host.EndsWith($".{item.Host}", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Host!.Length)
            .ThenBy(item => item.Item.Id)
            .Select(item => item.Item)
            .FirstOrDefault();

        try
        {
            // Use the list's exact address so its memory, disk and in-flight cache keys agree.
            var favicon = website?.Favicon ?? await faviconService.GetFaviconAsync(website?.Address ?? Address);
            if (favicon is not null)
            {
                _favicon = favicon;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Favicon)));
            }
        }
        catch
        {
            // Keep the original globe when loading fails; notification delivery is unaffected.
        }
    }
}
