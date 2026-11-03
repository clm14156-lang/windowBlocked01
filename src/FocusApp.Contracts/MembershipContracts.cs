namespace FocusApp.Contracts;
public static class MembershipIpc
{
    public const string Begin = "membership.begin";
    public const string Apply = "membership.apply";
    public const string Status = "membership.status";
    public const string Clear = "membership.clear";
}
public sealed record MembershipApplyCommand(string Token);
