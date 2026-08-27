using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Infrastructure.AccessControl;

namespace FocusApp.Service;

public sealed record AccessControlHostStartResult(bool WebsiteActive, bool ApplicationActive, int? ProxyPort);

public interface IAccessControlExecutionHost : IAsyncDisposable
{
    event EventHandler<AccessBlockedEvent>? AccessBlocked;

    event EventHandler<string>? ExecutionFailed;

    bool WebsiteActive { get; }

    bool ApplicationActive { get; }

    int? ProxyPort { get; }

    Task<AccessControlHostStartResult> StartAsync(
        IReadOnlyCollection<WebsiteAccessRule> websiteRules,
        IReadOnlyCollection<ApplicationAccessRule> applicationRules,
        CancellationToken cancellationToken = default);

    void UpdateRules(
        IReadOnlyCollection<WebsiteAccessRule> websiteRules,
        IReadOnlyCollection<ApplicationAccessRule> applicationRules);

    void ConfigureUpstream(UpstreamProxyConfigurationDto? upstreamProxy);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IAccessControlExecutionHostFactory
{
    IAccessControlExecutionHost Create(string userSid);
}

public sealed class AccessControlExecutionHostFactory : IAccessControlExecutionHostFactory
{
    public IAccessControlExecutionHost Create(string userSid) => new AccessControlExecutionHost(userSid);
}

public sealed class AccessControlExecutionHost : IAccessControlExecutionHost
{
    private readonly string _userSid;
    private readonly ILocalWebsiteProxy _websiteProxy;
    private readonly IApplicationControlMonitor _applicationMonitor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AccessControlExecutionHost(
        string userSid,
        ILocalWebsiteProxy? websiteProxy = null,
        IApplicationControlMonitor? applicationMonitor = null)
    {
        if (string.IsNullOrWhiteSpace(userSid))
        {
            throw new ArgumentException("Windows 用户 SID 不能为空。", nameof(userSid));
        }

        _userSid = userSid;
        _websiteProxy = websiteProxy ?? new LocalWebsiteProxy();
        _applicationMonitor = applicationMonitor ?? new ApplicationControlMonitor();
        _websiteProxy.WebsiteBlocked += WebsiteProxy_WebsiteBlocked;
        _applicationMonitor.ApplicationBlocked += ApplicationMonitor_ApplicationBlocked;
        _applicationMonitor.TerminationFailed += ApplicationMonitor_TerminationFailed;
        _applicationMonitor.ExecutionFailed += ApplicationMonitor_ExecutionFailed;
    }

    public event EventHandler<AccessBlockedEvent>? AccessBlocked;

    public event EventHandler<string>? ExecutionFailed;

    public bool WebsiteActive => _websiteProxy.IsRunning;

    public bool ApplicationActive => _applicationMonitor.IsRunning;

    public int? ProxyPort => _websiteProxy.Port;

    public async Task<AccessControlHostStartResult> StartAsync(
        IReadOnlyCollection<WebsiteAccessRule> websiteRules,
        IReadOnlyCollection<ApplicationAccessRule> applicationRules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(websiteRules);
        ArgumentNullException.ThrowIfNull(applicationRules);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var enabledWebsites = websiteRules.Where(rule => rule.IsEnabled).ToArray();
            var enabledApplications = applicationRules.Where(rule => rule.IsEnabled).ToArray();
            if (enabledWebsites.Length > 0)
            {
                await _websiteProxy.StartAsync(enabledWebsites, cancellationToken);
            }
            else
            {
                await _websiteProxy.StopAsync(cancellationToken);
            }

            if (enabledApplications.Length > 0)
            {
                await _applicationMonitor.StartAsync(_userSid, enabledApplications, cancellationToken);
            }
            else
            {
                await _applicationMonitor.StopAsync(cancellationToken);
            }

            return new AccessControlHostStartResult(WebsiteActive, ApplicationActive, ProxyPort);
        }
        catch
        {
            await StopCoreAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateRules(
        IReadOnlyCollection<WebsiteAccessRule> websiteRules,
        IReadOnlyCollection<ApplicationAccessRule> applicationRules)
    {
        _websiteProxy.UpdateRules(websiteRules.Where(rule => rule.IsEnabled).ToArray());
        _applicationMonitor.UpdateRules(applicationRules.Where(rule => rule.IsEnabled).ToArray());
    }

    public void ConfigureUpstream(UpstreamProxyConfigurationDto? upstreamProxy)
        => _websiteProxy.ConfigureUpstream(upstreamProxy);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _websiteProxy.WebsiteBlocked -= WebsiteProxy_WebsiteBlocked;
        _applicationMonitor.ApplicationBlocked -= ApplicationMonitor_ApplicationBlocked;
        _applicationMonitor.TerminationFailed -= ApplicationMonitor_TerminationFailed;
        _applicationMonitor.ExecutionFailed -= ApplicationMonitor_ExecutionFailed;
        await _websiteProxy.DisposeAsync();
        await _applicationMonitor.DisposeAsync();
        _gate.Dispose();
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        await _applicationMonitor.StopAsync(cancellationToken);
        await _websiteProxy.StopAsync(cancellationToken);
    }

    private void WebsiteProxy_WebsiteBlocked(object? sender, WebsiteBlockedEventArgs e)
    {
        AccessBlocked?.Invoke(this, new AccessBlockedEvent(
            BlockedTargetKind.Website,
            e.Result.RuleId!.Value,
            e.Result.RuleName ?? string.Empty,
            e.Host,
            DateTimeOffset.UtcNow));
    }

    private void ApplicationMonitor_ApplicationBlocked(object? sender, ApplicationBlockedEventArgs e)
    {
        AccessBlocked?.Invoke(this, new AccessBlockedEvent(
            BlockedTargetKind.Application,
            e.Result.RuleId!.Value,
            e.Result.RuleName ?? string.Empty,
            e.Process.ExecutablePath,
            DateTimeOffset.UtcNow));
    }

    private void ApplicationMonitor_TerminationFailed(object? sender, ApplicationBlockFailureEventArgs e)
        => ExecutionFailed?.Invoke(this, $"无法终止 {e.Process.ExecutablePath}：{e.ErrorMessage}");

    private void ApplicationMonitor_ExecutionFailed(object? sender, string error)
        => ExecutionFailed?.Invoke(this, error);
}
