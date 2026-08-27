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
        => isLoggedIn && isVip;

    public static FocusStartModeDecision EvaluateStart(
        bool isLoggedIn,
        bool isVip,
        bool isForcedModeRequested)
    {
        if (!isForcedModeRequested)
        {
            return FocusStartModeDecision.Normal;
        }

        if (!isLoggedIn)
        {
            return FocusStartModeDecision.LoginRequired;
        }

        return isVip
            ? FocusStartModeDecision.Forced
            : FocusStartModeDecision.VipRequired;
    }

    public static bool CanStart(FocusStartModeDecision decision)
        => decision is FocusStartModeDecision.Normal or FocusStartModeDecision.Forced;
}
