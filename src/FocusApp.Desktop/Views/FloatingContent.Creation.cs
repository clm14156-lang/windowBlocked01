using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class FloatingContent
{
    private FocusFloatingWindowViewModel? _creationModel;
    private DispatcherTimer? _creationTimer;
    private DispatcherOperation? _creationFocusOperation;
    private DispatcherOperation? _creationScrollOperation;
    private readonly Stopwatch _creationClock = new();
    private double _creationFromHeight;
    private double _creationFromTaskArea;
    private double _creationToHeight;
    private bool _creationNeedsFocus;
    internal Func<bool> CreationAnimationPreference { get; set; } = () => SystemParameters.ClientAreaAnimation;

    private void AttachCreationPresentation()
    {
        _creationModel = DataContext as FocusFloatingWindowViewModel;
        if (_creationModel is null) return;
        _creationModel.TaskCreationStateChanged += CreationStateChanged;
        _creationModel.TaskCreated += CreatedTask;
        SystemParameters.StaticPropertyChanged += CreationAnimationSettingsChanged;
    }

    private void DetachCreationPresentation()
    {
        CancelCreationPresentation();
        if (_creationModel is not null)
        {
            _creationModel.TaskCreationStateChanged -= CreationStateChanged;
            _creationModel.TaskCreated -= CreatedTask;
        }
        SystemParameters.StaticPropertyChanged -= CreationAnimationSettingsChanged;
        _creationModel = null;
    }

    private void CancelCreationPresentation()
    {
        _creationFocusOperation?.Abort();
        _creationScrollOperation?.Abort();
        _creationNeedsFocus = false;
        StopCreationTimer();
        _creationModel?.CancelTaskCreation();
        StopCreationTimer();
        _creationModel?.SetCreationPresentation(0);
    }

    private void Content_PreviewKeyDown(object sender, KeyEventArgs e) => HandleCreationKey(e);

    private void CreatedTask(object? sender, FocusTaskViewModel task)
    {
        _creationScrollOperation?.Abort();
        _creationScrollOperation = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || !IsVisible || _creationModel?.VisibleTasks.Contains(task) != true) return;
            UpdateLayout();
            TaskScrollViewer.ScrollToBottom();
        }, DispatcherPriority.Loaded);
    }

    internal void HandleCreationKey(KeyEventArgs e)
    {
        if (e.Handled || !IsVisible || DataContext is not FocusFloatingWindowViewModel model || !model.IsFocusing ||
            Keyboard.Modifiers != ModifierKeys.None || e.Key == Key.ImeProcessed) return;
        if (e.Key == Key.Tab)
        {
            if (model.IsCreatingTask || model.BeginTaskCreation())
            {
                e.Handled = true;
                QueueCreationFocus();
            }
        }
        else if (model.IsCreatingTask && e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            if (e.Key == Key.Enter) model.SubmitTaskCreation();
            else model.CancelTaskCreation();
        }
    }

    private void QueueCreationFocus()
    {
        _creationNeedsFocus = true;
        _creationFocusOperation?.Abort();
        _creationFocusOperation = Dispatcher.InvokeAsync(TryCreationFocus, DispatcherPriority.Input);
    }

    private void TryCreationFocus()
    {
        if (!_creationNeedsFocus || !IsLoaded || !IsVisible || _creationModel?.IsCreatingTask != true ||
            Window.GetWindow(this)?.IsActive != true) return;
        // The first expansion frame can still be clipped or rounded to zero at high DPI.
        UpdateLayout();
        TaskTitleInput.Focus();
        Keyboard.Focus(TaskTitleInput);
        _creationNeedsFocus = !TaskTitleInput.IsKeyboardFocused;
    }

    private void CreationStateChanged(object? sender, EventArgs e)
    {
        if (_creationModel is null) return;
        _creationFromHeight = _creationModel.CreationHeight;
        _creationFromTaskArea = _creationModel.TaskAreaHeight;
        _creationToHeight = _creationModel.IsCreatingTask ? FocusFloatingWindowViewModel.TaskCreationHeight : 0;
        StopCreationTimer();
        if (!IsLoaded || !IsVisible || !CreationAnimationPreference())
            _creationModel.SetCreationPresentation(_creationToHeight);
        else
        {
            _creationClock.Restart();
            _creationTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _creationTimer.Tick += CreationFrame;
            _creationTimer.Start();
            CreationFrame(this, EventArgs.Empty);
        }
        if (_creationModel.IsCreatingTask) QueueCreationFocus();
        else
        {
            _creationNeedsFocus = false;
            _creationFocusOperation?.Abort();
            if (TaskTitleInput.IsKeyboardFocusWithin) Focus();
        }
    }

    private void CreationFrame(object? sender, EventArgs e)
    {
        if (_creationModel is not { } model) { StopCreationTimer(); return; }
        var progress = Math.Clamp(_creationClock.Elapsed.TotalMilliseconds / 180, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var height = _creationFromHeight + (_creationToHeight - _creationFromHeight) * eased;
        // Recalculate the destination while tasks complete or arrive during the transition.
        var taskArea = _creationFromTaskArea + (model.GetTaskAreaHeightForCreation(_creationToHeight) - _creationFromTaskArea) * eased;
        model.SetCreationPresentation(height, taskArea);
        TryCreationFocus();
        if (progress < 1) return;
        StopCreationTimer();
        model.SetCreationPresentation(_creationToHeight);
    }

    private void CreationAnimationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation) || SystemParameters.ClientAreaAnimation) return;
        Dispatcher.Invoke(() => { StopCreationTimer(); _creationModel?.SetCreationPresentation(_creationModel.IsCreatingTask ? FocusFloatingWindowViewModel.TaskCreationHeight : 0); });
    }

    private void StopCreationTimer()
    {
        if (_creationTimer is null) return;
        _creationTimer.Stop();
        _creationTimer.Tick -= CreationFrame;
        _creationTimer = null;
        _creationClock.Stop();
    }
}
