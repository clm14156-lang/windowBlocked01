namespace FocusApp.Desktop.ViewModels;

public enum MembershipType
{
    Normal,
    Annual,
    Lifetime
}

public sealed class LoginSucceededEventArgs(string account, MembershipType membershipType) : EventArgs
{
    public string Account { get; } = account;

    public MembershipType MembershipType { get; } = membershipType;
}
