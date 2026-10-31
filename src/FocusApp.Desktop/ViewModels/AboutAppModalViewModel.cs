using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AboutAppModalViewModel : INotifyPropertyChanged
{
    public AboutAppModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        // Only visual interaction is requested; update services and URLs are not connected.
        CheckUpdateCommand = new RelayCommand<object>(_ => { });
        OpenLinkCommand = new RelayCommand<object>(_ => { });
        var assembly = typeof(AboutAppModalViewModel).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        Version = $"v{version}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen { get; private set; }
    public string Version { get; }
    public string CopyrightText => $"© {DateTime.Now.Year} 专注时光 · 让专注自然发生";
    public ICommand CloseCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenLinkCommand { get; }

    public void Open()
    {
        IsOpen = true;
        PropertyChanged?.Invoke(this, new(nameof(CopyrightText)));
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }
    public void Close()
    {
        IsOpen = false;
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }
}
