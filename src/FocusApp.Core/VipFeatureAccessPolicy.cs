namespace FocusApp.Core;

public static class VipFeatureAccessPolicy
{
    public static bool CanUse(bool isLoggedIn, bool isVip) => isLoggedIn && isVip;
}
