namespace FocusApp.Core;

/// <summary>Shared quota for subtask creation on all focus task surfaces.</summary>
public static class SubTaskCreationPolicy
{
    public const int FreeLimit = 5;
    public const int MemberLimit = 20;

    public static int GetLimit(bool isVipMember) => isVipMember ? MemberLimit : FreeLimit;

    public static bool CanAdd(int currentCount, bool isVipMember) => currentCount < GetLimit(isVipMember);

    public static bool CanSave(IReadOnlyCollection<string> existingIds, IReadOnlyCollection<string> proposedIds, bool isVipMember)
    {
        if (proposedIds.Count > MemberLimit) return false;
        if (proposedIds.Count <= GetLimit(isVipMember)) return true;
        // Expiry must not prevent editing or removing existing children. New IDs
        // above the quota are rejected even if the total count has not increased.
        var existing = existingIds.ToHashSet(StringComparer.Ordinal);
        return proposedIds.All(existing.Contains);
    }
}
