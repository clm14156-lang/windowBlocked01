using System.ComponentModel;
using System.Windows;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FocusFlowView
{
    private readonly FocusMeteorSchedule _meteorSchedule = new();
    private FocusSessionViewModel? _meteorSession;
    private Window? _meteorWindow;

    private void InitializeMeteor()
    {
        FocusMeteor.PlaybackFinished += (_, _) => UpdateMeteor();
        Loaded += (_, _) =>
        {
            _meteorWindow = Window.GetWindow(this);
            if (_meteorWindow is not null) _meteorWindow.StateChanged += MeteorWindowStateChanged;
            ObserveMeteorSession(DataContext as FocusSessionViewModel);
        };
        Unloaded += (_, _) =>
        {
            if (_meteorWindow is not null) _meteorWindow.StateChanged -= MeteorWindowStateChanged;
            _meteorWindow = null;
            ObserveMeteorSession(null);
        };
        DataContextChanged += (_, args) =>
        {
            if (IsLoaded) ObserveMeteorSession(args.NewValue as FocusSessionViewModel);
        };
        IsVisibleChanged += (_, _) => UpdateMeteor();
    }

    private void ObserveMeteorSession(FocusSessionViewModel? session)
    {
        if (ReferenceEquals(_meteorSession, session)) return;
        if (_meteorSession is not null) _meteorSession.PropertyChanged -= MeteorSessionChanged;
        FocusMeteor.Stop();
        _meteorSession = session;
        if (session is not null) session.PropertyChanged += MeteorSessionChanged;
        UpdateMeteor();
    }

    private void MeteorWindowStateChanged(object? sender, EventArgs e) => UpdateMeteor();

    private void MeteorSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSessionViewModel.Stage) or nameof(FocusSessionViewModel.ElapsedFocusSeconds) or
            nameof(FocusSessionViewModel.IsDarkFocusTheme) or nameof(FocusSessionViewModel.IsEndConfirmationOpen) or
            nameof(FocusSessionViewModel.AuthoritativeSessionId))
            UpdateMeteor();
    }

    private bool CanShowMeteor => IsLoaded && IsVisible && _meteorWindow?.WindowState != WindowState.Minimized &&
        _meteorSession is { IsFocusing: true, IsDarkFocusTheme: true };

    private void UpdateMeteor()
    {
        var session = _meteorSession;
        _meteorSchedule.SelectSession(session is { Stage: FocusFlowStage.Preparing or FocusFlowStage.Focusing }
            ? session.MeteorSessionKey : null);
        if (!CanShowMeteor || session is null)
        {
            FocusMeteor.Stop();
            return;
        }

        FocusMeteor.SetPaused(session.IsEndConfirmationOpen);
        if (!session.IsEndConfirmationOpen && !FocusMeteor.IsPlaying &&
            _meteorSchedule.TryConsume(session.ElapsedFocusSeconds, session.RemainingFocusSeconds))
            FocusMeteor.Play(_meteorSchedule.AnimationSeconds);
    }

#if DEBUG
    internal bool TryPlayMeteorForDebug()
    {
        if (!CanShowMeteor || _meteorSession!.IsEndConfirmationOpen) return false;
        if (!FocusMeteor.IsPlaying)
            FocusMeteor.Play(Random.Shared.Next(FocusMeteorSchedule.MinimumDuration, FocusMeteorSchedule.MaximumDuration + 1));
        return true;
    }
#endif
}
