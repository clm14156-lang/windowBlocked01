using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class AccessControlServiceTests
{
    private readonly AccessControlService _service = new();

    [Fact]
    public void Website_MatchesExactHostIgnoringSchemePathAndCase()
    {
        var rule = new WebsiteAccessRule(Guid.NewGuid(), "Example", "example.com");

        var result = _service.EvaluateWebsite("HTTPS://EXAMPLE.COM/path?q=1", [rule]);

        Assert.True(result.IsBlocked);
        Assert.Equal(AccessRuleType.Website, result.RuleType);
        Assert.Equal(rule.Id, result.RuleId);
    }

    [Fact]
    public void Website_MatchesSubdomainButNotSimilarSuffix()
    {
        var rule = new WebsiteAccessRule(Guid.NewGuid(), "Example", "example.com");

        Assert.True(_service.EvaluateWebsite("www.example.com", [rule]).IsBlocked);
        Assert.False(_service.EvaluateWebsite("notexample.com", [rule]).IsBlocked);
    }

    [Fact]
    public void Website_MostSpecificEnabledRuleWins()
    {
        var parent = new WebsiteAccessRule(Guid.NewGuid(), "Parent", "example.com");
        var child = new WebsiteAccessRule(Guid.NewGuid(), "Child", "admin.example.com");

        var result = _service.EvaluateWebsite("https://admin.example.com/login", [parent, child]);

        Assert.Equal(child.Id, result.RuleId);
        Assert.Equal("Child", result.RuleName);
    }

    [Fact]
    public void DisabledWebsiteRuleDoesNotBlock()
    {
        var rule = new WebsiteAccessRule(Guid.NewGuid(), "Example", "example.com", false);

        Assert.True(_service.EvaluateWebsite("example.com", [rule]).IsAllowed);
    }

    [Fact]
    public void Application_MatchesWindowsPathCaseInsensitively()
    {
        var rule = new ApplicationAccessRule(Guid.NewGuid(), "Editor", @"C:\Apps\Editor\Editor.exe");

        var result = _service.EvaluateApplication(@"c:/apps/editor/editor.EXE", [rule]);

        Assert.True(result.IsBlocked);
        Assert.Equal(AccessRuleType.Application, result.RuleType);
        Assert.Equal(rule.Id, result.RuleId);
    }

    [Fact]
    public void Application_DoesNotMatchDifferentExecutableWithSameName()
    {
        var rule = new ApplicationAccessRule(Guid.NewGuid(), "Editor", @"C:\Apps\Editor\Editor.exe");

        Assert.False(_service.EvaluateApplication(@"C:\Other\Editor.exe", [rule]).IsBlocked);
    }

    [Fact]
    public void UnifiedEvaluationReturnsAllowedForWrongOrUnknownInput()
    {
        var result = _service.Evaluate(
            AccessRequest.Application(@"C:\Apps\Editor.exe"),
            [new WebsiteAccessRule(Guid.NewGuid(), "Site", "example.com")],
            []);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RuleId);
    }
}
