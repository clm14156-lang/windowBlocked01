using System.ComponentModel;
using FocusApp.Core;

namespace FocusApp.Desktop.ViewModels;

/// <summary>
/// Coordinates runtime-only state shared by the focus, rule, blocking and
/// statistics view models. Each feature keeps its own business rules; this
/// class only routes their completed state changes to the other consumers.
/// </summary>
public sealed class FocusStateCoordinator : IDisposable
{
    private readonly HomePageViewModel _homePage;
    private readonly SettingsPageViewModel _settingsPage;
    private readonly BlockingPageViewModel _blockingPage;
    private readonly StatisticsOverviewViewModel _statisticsPage;
    private readonly HashSet<FocusSessionRecord> _recordedCompletions = new(ReferenceEqualityComparer.Instance);
    private bool _isDisposed;

    public FocusStateCoordinator(
        HomePageViewModel homePage,
        SettingsPageViewModel settingsPage,
        BlockingPageViewModel blockingPage,
        StatisticsOverviewViewModel statisticsPage)
    {
        _homePage = homePage ?? throw new ArgumentNullException(nameof(homePage));
        _settingsPage = settingsPage ?? throw new ArgumentNullException(nameof(settingsPage));
        _blockingPage = blockingPage ?? throw new ArgumentNullException(nameof(blockingPage));
        _statisticsPage = statisticsPage ?? throw new ArgumentNullException(nameof(statisticsPage));

        _homePage.FocusSession.CompletionRecorded += FocusSession_CompletionRecorded;
        _settingsPage.RulesChanged += SettingsPage_RulesChanged;
        _blockingPage.BlockingChanged += BlockingPage_BlockingChanged;
        if (_settingsPage.ForcedModeItem is not null)
        {
            _settingsPage.ForcedModeItem.PropertyChanged += ForcedModeItem_PropertyChanged;
        }

        RefreshSharedState();
    }

    /// <summary>
    /// Re-evaluates the in-memory automatic rules. The timer host calls this
    /// method; scheduling remains in HomePageViewModel and Core.
    /// </summary>
    public void EvaluateAutomaticBlocking(DateTime? now = null)
    {
        ThrowIfDisposed();
        _homePage.EvaluateAutomaticBlocking(
            _settingsPage.AutomaticRules,
            _settingsPage.IsAutomaticBlockingEnabled,
            now);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _homePage.FocusSession.CompletionRecorded -= FocusSession_CompletionRecorded;
        _settingsPage.RulesChanged -= SettingsPage_RulesChanged;
        _blockingPage.BlockingChanged -= BlockingPage_BlockingChanged;
        if (_settingsPage.ForcedModeItem is not null)
        {
            _settingsPage.ForcedModeItem.PropertyChanged -= ForcedModeItem_PropertyChanged;
        }
    }

    private void RefreshSharedState(DateTime? now = null)
    {
        SynchronizeForcedMode();
        RefreshBlockingPreview();
        EvaluateAutomaticBlocking(now);
    }

    private void FocusSession_CompletionRecorded(object? sender, FocusSessionCompletedEventArgs e)
    {
        if (_recordedCompletions.Add(e.Record))
        {
            _statisticsPage.AddCompletedFocusSession(e.Record, e.CompletedTaskNames);
        }
    }

    private void SettingsPage_RulesChanged(object? sender, EventArgs e)
        => EvaluateAutomaticBlocking();

    private void BlockingPage_BlockingChanged(object? sender, EventArgs e)
        => RefreshBlockingPreview();

    private void ForcedModeItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsToggleItemViewModel.IsEnabled))
        {
            SynchronizeForcedMode();
        }
    }

    private void SynchronizeForcedMode()
        => _homePage.SetForcedModeEnabled(_settingsPage.ForcedModeItem?.IsEnabled == true);

    private void RefreshBlockingPreview()
        => _homePage.UpdateBlockingContent(_blockingPage.Websites, _blockingPage.Applications);

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(FocusStateCoordinator));
        }
    }
}
