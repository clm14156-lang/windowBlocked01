using FocusApp.Core;
using Xunit;

namespace FocusApp.Tests.Core;

public sealed class ForcedModeAccessPolicyTests
{
    [Theory]
    [InlineData(false, false, false, FocusStartModeDecision.Normal)]
    [InlineData(false, false, true, FocusStartModeDecision.LoginRequired)]
    [InlineData(true, false, true, FocusStartModeDecision.VipRequired)]
    [InlineData(true, true, true, FocusStartModeDecision.Forced)]
    public void EvaluateStart_ReturnsTheExpectedDecision(
        bool isLoggedIn,
        bool isVip,
        bool isForcedModeRequested,
        FocusStartModeDecision expected)
    {
        var decision = ForcedModeAccessPolicy.EvaluateStart(isLoggedIn, isVip, isForcedModeRequested);

        Assert.Equal(expected, decision);
        Assert.Equal(
            expected is FocusStartModeDecision.Normal or FocusStartModeDecision.Forced,
            ForcedModeAccessPolicy.CanStart(decision));
    }
}
