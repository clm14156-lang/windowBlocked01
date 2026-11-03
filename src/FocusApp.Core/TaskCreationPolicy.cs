namespace FocusApp.Core;

/// <summary>Concurrent unfinished root tasks for the currently selected goal.</summary>
public static class TaskCreationPolicy
{
    public const int FreeLimit = 10;
    public const int MemberLimit = 99;
    public static int GetLimit(bool isVipMember) => isVipMember ? MemberLimit : FreeLimit;
    public static bool CanCreate(int pendingCount, bool isVipMember) => pendingCount < GetLimit(isVipMember);
}
