namespace FocusApp.Core;

public enum FocusStartModeDecision
{
    Normal,
    Forced,
    LoginRequired,
    VipRequired
}

public static class ForcedModeAccessPolicy
{
    public static bool CanUseForcedMode(bool isLoggedIn, bool isVip)
        => true;

    public static FocusStartModeDecision EvaluateStart(
        bool isLoggedIn,
        bool isVip,
        bool isForcedModeRequested)
        => isForcedModeRequested ? FocusStartModeDecision.Forced : FocusStartModeDecision.Normal;

    public static bool CanStart(FocusStartModeDecision decision)
        => decision is FocusStartModeDecision.Normal or FocusStartModeDecision.Forced;
}
