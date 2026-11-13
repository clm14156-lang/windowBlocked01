using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using FocusApp.Desktop.Services.Cloud;

namespace FocusApp.Desktop.ViewModels;

public sealed class AboutAppModalViewModel : INotifyPropertyChanged
{
    public AboutAppModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        CheckUpdateCommand = new RelayCommand<object>(_ => FeedbackMessage = "检查更新功能暂未开放");
        OpenLinkCommand = new RelayCommand<string>(OpenLink);
        var assembly = typeof(AboutAppModalViewModel).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        Version = $"v{version}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen { get; private set; }
    public string Version { get; }
    public string CopyrightText => "© 2026 专注时光";
    private string _feedbackMessage = string.Empty;
    public string FeedbackMessage
    {
        get => _feedbackMessage;
        private set
        {
            if (_feedbackMessage == value) return;
            _feedbackMessage = value;
            PropertyChanged?.Invoke(this, new(nameof(FeedbackMessage)));
        }
    }
    public ICommand CloseCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenLinkCommand { get; }

    public void Open()
    {
        FeedbackMessage = string.Empty;
        IsOpen = true;
        PropertyChanged?.Invoke(this, new(nameof(CopyrightText)));
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }
    public void Close()
    {
        IsOpen = false;
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }

    private void OpenLink(string? entry)
    {
        var address = entry switch
        {
            "Website" => AccountWebsiteLinks.OfficialWebsite,
            "Help" => AccountWebsiteLinks.HelpCenter,
            "Feedback" => AccountWebsiteLinks.Feedback,
            "Contact" => AccountWebsiteLinks.Contact,
            "Privacy" => AccountWebsiteLinks.PrivacyPolicy,
            "Terms" => AccountWebsiteLinks.TermsOfService,
            _ => null
        };
        if (address is null)
        {
            FeedbackMessage = entry == "Contact" ? "联系方式暂未配置" : "功能即将开放";
            return;
        }
        try { AccountWebsiteLinks.Open(address); FeedbackMessage = string.Empty; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { FeedbackMessage = "无法打开链接，请稍后重试。"; }
    }
}
