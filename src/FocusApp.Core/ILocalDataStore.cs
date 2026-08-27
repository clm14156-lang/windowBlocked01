namespace FocusApp.Core;

public interface ILocalDataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<LocalDataSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveTargetAsync(
        LocalTarget target,
        IReadOnlyCollection<LocalTask> tasks,
        CancellationToken cancellationToken = default);

    Task DeleteTargetAsync(string targetId, CancellationToken cancellationToken = default);

    Task SaveFocusSessionAsync(
        LocalFocusSession session,
        IReadOnlyCollection<string>? completedTaskIdsToMark = null,
        CancellationToken cancellationToken = default);

    Task ReplaceWebsiteRulesAsync(
        IReadOnlyCollection<LocalWebsiteRule> rules,
        CancellationToken cancellationToken = default);

    Task ReplaceApplicationRulesAsync(
        IReadOnlyCollection<LocalApplicationRule> rules,
        CancellationToken cancellationToken = default);

    Task ReplaceAutomaticRulesAsync(
        IReadOnlyCollection<LocalAutomaticRule> rules,
        CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(
        LocalAppSettings settings,
        IReadOnlyCollection<LocalDurationPreset> durationPresets,
        IReadOnlyCollection<LocalMonthlyFocusTarget> monthlyFocusTargets,
        CancellationToken cancellationToken = default);
}
