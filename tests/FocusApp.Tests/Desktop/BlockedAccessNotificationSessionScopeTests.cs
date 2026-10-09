using FocusApp.Contracts;
using FocusApp.Core;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class BlockedAccessNotificationSessionScopeTests
{
    [Fact]
    public void DismissOnlyClosesCurrentToast_AndLaterWebsiteNotificationsRemainEnabled()
    {
        var focus = CreateFocus();
        focus.Start(1);
        using var scope = new BlockedAccessNotificationSessionScope(focus);
        var token = scope.CurrentSessionToken;
        var closeCount = 0;
        var toast = CreateToast(scope, token);
        toast.CloseRequested += (_, _) => closeCount++;
        toast.CloseCommand.Execute(null);
        Assert.Equal(1, closeCount);
        Assert.True(scope.ShouldShow("Website"));
        Assert.Same(token, scope.CurrentSessionToken);
    }

    [Fact]
    public void SuppressClosesCurrentToast_AndOnlySuppressesWebsitePresentation()
    {
        var focus = CreateFocus();
        focus.Start(1);
        using var scope = new BlockedAccessNotificationSessionScope(focus);
        var toast = CreateToast(scope, scope.CurrentSessionToken);
        var closed = false;
        toast.CloseRequested += (_, _) => closed = true;
        toast.SuppressForSessionCommand.Execute(null);
        Assert.True(closed);
        Assert.False(scope.ShouldShow("Website"));
        Assert.False(scope.ShouldShow("website"));
        Assert.True(scope.ShouldShow("Application"));
        var rule = new WebsiteAccessRule(Guid.NewGuid(), "Example", "example.com");
        Assert.True(new AccessControlService().EvaluateWebsite("https://example.com", [rule]).IsBlocked);
    }

    [Fact]
    public void MuteSurvivesPreparationAndEndConfirmation_ButExpiresOnCompletionAndRestart()
    {
        var focus = CreateFocus();
        focus.Start(1);
        using var scope = new BlockedAccessNotificationSessionScope(focus);
        var token = scope.CurrentSessionToken;
        scope.SuppressWebsiteNotifications(token);
        Advance(focus,5);
        Assert.Same(token,scope.CurrentSessionToken);
        focus.RequestEndCommand.Execute(null);
        Assert.False(scope.ShouldShow("Website"));
        focus.ContinueFocusCommand.Execute(null);
        Assert.False(scope.ShouldShow("Website"));
        Advance(focus,60);
        Assert.True(focus.IsCompleted);
        Assert.True(scope.ShouldShow("Website"));
        Assert.Null(scope.CurrentSessionToken);
        focus.Start(1);
        Assert.True(scope.ShouldShow("Website"));
        Assert.NotSame(token,scope.CurrentSessionToken);
        scope.SuppressWebsiteNotifications(token); // A stale Toast must not mute the new session.
        Assert.True(scope.ShouldShow("Website"));
    }

    [Fact]
    public void CancelPreparationAndDiscardFocusClearTheTemporaryPreference()
    {
        var focus = CreateFocus();
        using var scope = new BlockedAccessNotificationSessionScope(focus);
        focus.Start(1);
        scope.SuppressWebsiteNotifications(scope.CurrentSessionToken);
        focus.CancelPreparationCommand.Execute(null);
        Assert.True(scope.ShouldShow("Website"));
        focus.Start(1);
        Advance(focus,5);
        scope.SuppressWebsiteNotifications(scope.CurrentSessionToken);
        focus.RequestEndCommand.Execute(null);
        focus.DiscardEndCommand.Execute(null);
        Assert.Equal(FocusFlowStage.Idle,focus.Stage);
        Assert.True(scope.ShouldShow("Website"));
    }

    [Fact]
    public void ForcedSessionRefreshRetainsMute_NewSessionIdRestoresItEvenAtTheSameStage()
    {
        var focus = CreateFocus();
        var now = new DateTimeOffset(2026,10,9,8,0,0,TimeSpan.Zero);
        var session = new LocalFocusSessionDto(Guid.NewGuid(),LocalFocusSessionStatusDto.Focusing,true,600,0,
            now.AddSeconds(-5),now,now.AddMinutes(10),null,null,null,null,false,null,null,[]);
        focus.ApplyAuthoritativeSession(session,nowUtc:now);
        using var scope = new BlockedAccessNotificationSessionScope(focus);
        var token = scope.CurrentSessionToken;
        scope.SuppressWebsiteNotifications(token);
        focus.ApplyAuthoritativeSession(session,nowUtc:now.AddSeconds(1));
        Assert.Same(token,scope.CurrentSessionToken);
        Assert.False(scope.ShouldShow("Website"));
        focus.ApplyAuthoritativeSession(session with { SessionId = Guid.NewGuid() },nowUtc:now.AddSeconds(2));
        Assert.Equal(FocusFlowStage.Focusing,focus.Stage);
        Assert.True(scope.ShouldShow("Website"));
        Assert.NotSame(token,scope.CurrentSessionToken);
    }

    [Fact]
    public void NoCurrentSessionOrApplicationToastCannotDisableFutureWebsiteNotifications()
    {
        var focus = CreateFocus();
        using var scope = new BlockedAccessNotificationSessionScope(focus);
        scope.SuppressWebsiteNotifications(null);
        Assert.True(scope.ShouldShow("Website"));
        var website = new BlockedAccessNotificationViewModel("Example","example.com","Website","当前网站");
        Assert.False(website.SuppressForSessionCommand.CanExecute(null));
        var application = new BlockedAccessNotificationViewModel("Editor","editor.exe","Application","当前应用",()=>throw new Exception("Unexpected suppression"));
        Assert.False(application.SuppressForSessionCommand.CanExecute(null));
        application.SuppressForSessionCommand.Execute(null);
        focus.Start(1);
        Assert.True(scope.ShouldShow("Website"));
    }

    private static FocusSessionViewModel CreateFocus() => new(runTimer:false);
    private static BlockedAccessNotificationViewModel CreateToast(BlockedAccessNotificationSessionScope scope,object? token)
        => new("Example","example.com","Website","当前网站",()=>scope.SuppressWebsiteNotifications(token));
    private static void Advance(FocusSessionViewModel focus,int seconds) { for(var i=0;i<seconds;i++) focus.AdvanceOneSecond(); }
}
