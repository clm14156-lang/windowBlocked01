using System.ComponentModel;
using System.Windows.Input;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class BrowserExtensionModalViewModel : INotifyPropertyChanged
{
    private Action? _continueFocus;
    private bool _skipAutomaticPromptForThisRun;
    private readonly IBrowserExtensionService _chrome;
    private readonly IBrowserExtensionService _edge;
    private readonly RelayCommand<object> _installCommand;
    private Task? _detection;
    private BrowserExtensionInstallationStatus _chromeStatus = BrowserExtensionInstallationStatus.Unknown;
    private BrowserExtensionInstallationStatus _edgeStatus = BrowserExtensionInstallationStatus.Unknown;
    private bool _hasCheckedInstallation;
    private bool _checkingInstallation;
    private bool _checkingForFocus;
    private int _openRequest;
    private string? _installError;

    public BrowserExtensionModalViewModel(IBrowserExtensionService? chrome = null, IBrowserExtensionService? edge = null)
    {
        _chrome = chrome ?? new ChromeExtensionService();
        _edge = edge ?? new EdgeExtensionService();
        CloseCommand = new RelayCommand<object>(_ => Close());
        SkipCommand = new RelayCommand<object>(_ => ContinueFocus());
        _installCommand = new RelayCommand<object>(Install, browser => (_hasCheckedInstallation || !_checkingInstallation) &&
            (Equals(browser, "Chrome") && !IsChromeInstalled || Equals(browser, "Edge") && !IsEdgeInstalled));
        InstallCommand = _installCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen { get; private set; }
    public bool ShowSkip { get; private set; }
    public ICommand CloseCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand InstallCommand { get; }
    public bool IsChromeInstalled => _chromeStatus == BrowserExtensionInstallationStatus.Installed;
    public bool IsEdgeInstalled => _edgeStatus == BrowserExtensionInstallationStatus.Installed;
    private bool HasInstalledExtension => IsChromeInstalled || IsEdgeInstalled;
    public string ChromeInstallButtonText => IsChromeInstalled ? "已安装 Chrome 插件" : "安装 Chrome 插件";
    public string EdgeInstallButtonText => IsEdgeInstalled ? "已安装 Edge 插件" : "安装 Edge 插件";
    public string Description => _installError ?? (IsChromeInstalled && IsEdgeInstalled
        ? "已检测到 Chrome 和 Edge 插件。\n可在浏览器扩展管理中确认启用状态。"
        : IsChromeInstalled
        ? "已检测到 Chrome 插件。\n可在 Chrome 扩展管理中确认启用状态。"
        : IsEdgeInstalled
        ? "已检测到 Edge 插件。\n可在 Edge 扩展管理中确认启用状态。"
        : _hasCheckedInstallation && (_chromeStatus == BrowserExtensionInstallationStatus.Unknown || _edgeStatus == BrowserExtensionInstallationStatus.Unknown)
            ? "暂时无法读取部分浏览器的插件状态，\n可在浏览器扩展管理中确认安装。"
            : "安装浏览器插件后，访问被屏蔽的网站会自动跳转到\nFocus 专注提醒页。");

    public void OpenFromSettings()
    {
        _openRequest++;
        _checkingForFocus = false;
        _installError = null;
        Open(null);
        _ = RefreshInstallationAsync();
    }
    public void OpenForFocus(Action continueFocus) => _ = OpenForFocusAsync(continueFocus);

    internal async Task OpenForFocusAsync(Action continueFocus)
    {
        if (_skipAutomaticPromptForThisRun)
        {
            continueFocus();
            return;
        }
        if (IsOpen || _checkingForFocus) return;
        _checkingForFocus = true;
        await CheckBeforeFocusAsync(++_openRequest, continueFocus);
    }

    private async Task CheckBeforeFocusAsync(int request, Action continueFocus)
    {
        await RefreshInstallationAsync();
        if (request != _openRequest) return;
        _checkingForFocus = false;
        if (HasInstalledExtension) continueFocus();
        else Open(continueFocus);
    }

    public Task RefreshInstallationAsync()
    {
        if (_detection is { IsCompleted: false }) return _detection;
        return _detection = RefreshInstallationCoreAsync();
    }

    private async Task RefreshInstallationCoreAsync()
    {
        _checkingInstallation = true;
        _installCommand.NotifyCanExecuteChanged();
        try
        {
            var statuses = await Task.WhenAll(_chrome.DetectInstallationAsync(), _edge.DetectInstallationAsync());
            _chromeStatus = statuses[0];
            _edgeStatus = statuses[1];
            _hasCheckedInstallation = true;
            PropertyChanged?.Invoke(this, new(nameof(IsChromeInstalled)));
            PropertyChanged?.Invoke(this, new(nameof(IsEdgeInstalled)));
            PropertyChanged?.Invoke(this, new(nameof(ChromeInstallButtonText)));
            PropertyChanged?.Invoke(this, new(nameof(EdgeInstallButtonText)));
            PropertyChanged?.Invoke(this, new(nameof(Description)));
            if (HasInstalledExtension && IsOpen && ShowSkip)
            {
                var continuation = _continueFocus;
                Close();
                continuation?.Invoke();
            }
        }
        finally
        {
            _checkingInstallation = false;
            _installCommand.NotifyCanExecuteChanged();
        }
    }

    private void Install(object? browser)
    {
        if (!_installCommand.CanExecute(browser)) return;
        var isChrome = Equals(browser, "Chrome");
        var service = isChrome ? _chrome : _edge;
        var browserName = isChrome ? "Chrome" : "Edge";
        try { service.OpenInstallPage(); _installError = null; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        { _installError = $"无法打开 {browserName}，请确认浏览器已安装，\n然后重试安装插件。"; }
        PropertyChanged?.Invoke(this, new(nameof(Description)));
    }

    private void Open(Action? continueFocus)
    {
        _installError = null;
        _continueFocus = continueFocus;
        ShowSkip = continueFocus is not null;
        IsOpen = true;
        PropertyChanged?.Invoke(this, new(nameof(ShowSkip)));
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
        PropertyChanged?.Invoke(this, new(nameof(Description)));
    }

    public void Close()
    {
        _openRequest++;
        _checkingForFocus = false;
        _continueFocus = null;
        IsOpen = false;
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }

    private void ContinueFocus()
    {
        if (!IsOpen || !ShowSkip) return;
        _skipAutomaticPromptForThisRun = true;
        var continuation = _continueFocus;
        Close();
        continuation?.Invoke();
    }
}
