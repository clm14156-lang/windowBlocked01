using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using static FocusApp.Desktop.Views.TaskCompletionMotion;

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
    private bool _inlineCreationPresentation;
    private FocusTaskViewModel? _hoveredCreationTask;
    internal Func<bool> CreationAnimationPreference { get; set; } = () => SystemParameters.ClientAreaAnimation;

    private void AttachCreationPresentation()
    {
        _creationModel = DataContext as FocusFloatingWindowViewModel;
        if (_creationModel is null) return;
        _creationModel.TaskCreationStateChanged += CreationStateChanged;
        _creationModel.TaskCreated += CreatedTask;
        _creationModel.SubTaskCreated += CreatedSubTask;
        _creationModel.PropertyChanged += CreationQuotaChanged;
        SystemParameters.StaticPropertyChanged += CreationAnimationSettingsChanged;
    }

    private void DetachCreationPresentation()
    {
        CancelCreationPresentation();
        if (_creationModel is not null)
        {
            _creationModel.TaskCreationStateChanged -= CreationStateChanged;
            _creationModel.TaskCreated -= CreatedTask;
            _creationModel.SubTaskCreated -= CreatedSubTask;
            _creationModel.PropertyChanged -= CreationQuotaChanged;
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
        _inlineCreationPresentation = false;
        _hoveredCreationTask = null;
    }

    private void Content_PreviewKeyDown(object sender, KeyEventArgs e) => HandleCreationKey(e);

    private void TaskRow_CreationMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FocusTaskViewModel task }) _hoveredCreationTask = task;
    }

    private void TaskRow_CreationMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement row && ReferenceEquals(row.DataContext, _hoveredCreationTask))
            _hoveredCreationTask = null;
    }

    private void CreatedTask(object? sender, FocusTaskViewModel task)
    {
        DesktopDiagnostics.RecordAction("Floating.TaskCreated", $"VisibleTasks={_creationModel?.VisibleTasks.Count}");
        _creationScrollOperation?.Abort();
        _creationScrollOperation = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || !IsVisible || _creationModel?.VisibleTasks.Contains(task) != true) return;
            DesktopDiagnostics.RecordAction("Floating.CreatedTaskScroll");
            UpdateLayout();
            TaskScrollViewer.ScrollToBottom();
        }, DispatcherPriority.Loaded);
    }

    private void CreatedSubTask(object? sender, FocusTaskViewModel parent) => QueueCreationFocus();

    private void CreationQuotaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusFloatingWindowViewModel.CanCreateSubTask) && _creationModel?.IsCreatingSubTask == true)
            QueueCreationFocus();
        if (e.PropertyName == nameof(FocusFloatingWindowViewModel.CanCreateRootTask) && _creationModel?.IsCreatingRootTask == true)
            QueueCreationFocus();
    }

    internal void HandleCreationKey(KeyEventArgs e)
    {
        if (IsTaskReordering)
        {
            if (e.Key == Key.Escape) ResetTaskDrag();
            e.Handled = true;
            return;
        }
        if (e.Handled || !IsVisible || DataContext is not FocusFloatingWindowViewModel model || !model.IsFocusing ||
            Keyboard.Modifiers != ModifierKeys.None || e.Key == Key.ImeProcessed) return;
        if (model.IsEditingTask)
        {
            if (e.Key is Key.Enter or Key.Escape or Key.Tab)
            {
                e.Handled = true;
                if (e.IsRepeat) return;
                if (e.Key == Key.Enter) model.SubmitTaskEdit();
                else if (e.Key == Key.Escape) model.CancelTaskEdit();
            }
            return;
        }
        if (e.Key == Key.Tab)
        {
            if (model.IsCreatingSubTask && !model.CanCreateSubTask) return;
            DesktopDiagnostics.RecordAction("Floating.Tab", $"Creating={model.IsCreatingTask}; Repeat={e.IsRepeat}; Active={Window.GetWindow(this)?.IsActive}");
            var hoveredParent = _hoveredCreationTask is { } hovered && model.VisibleTasks.Contains(hovered) ? hovered : null;
            if (model.IsCreatingTask || (hoveredParent is null ? model.BeginTaskCreation() : model.BeginSubTaskCreation(hoveredParent)))
            {
                e.Handled = true;
                QueueCreationFocus();
            }
            else if (hoveredParent is not null) e.Handled = true;
        }
        else if (model.IsCreatingTask && e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            if (e.Key == Key.Enter)
            {
                DesktopDiagnostics.RecordAction("Floating.TaskSubmit", $"TitleLength={model.TaskTitle?.Length ?? 0}");
                var submitted = model.SubmitTaskCreation();
                DesktopDiagnostics.RecordAction("Floating.TaskSubmitResult", $"Submitted={submitted}");
                if (model.IsCreatingTask) QueueCreationFocus();
            }
            else
            {
                DesktopDiagnostics.RecordAction("Floating.TaskCancel");
                model.CancelTaskCreation();
            }
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
        DesktopDiagnostics.RecordAction("Floating.EditorFocusBegin");
        // The first expansion frame can still be clipped or rounded to zero at high DPI.
        UpdateLayout();
        var input = CreationInput();
        if (_creationModel.IsCreatingRootTask && !_creationModel.CanCreateRootTask)
        {
            if (TaskTitleInput.IsKeyboardFocusWithin) Focus();
            _creationNeedsFocus = false;
            return;
        }
        if (_creationModel.IsCreatingSubTask && _creationModel.SubTaskCreationParent is { } parent &&
            TaskRows().Where(row => ReferenceEquals(row.DataContext, parent))
                .Select(row => FindCompletionElement<Border>(row, "InlineSubTaskCreation")).FirstOrDefault() is { } editor)
        {
            if (_creationModel.CanCreateSubTask && input is { IsVisible: true, IsEnabled: true })
            {
                input.Focus();
                Keyboard.Focus(input);
            }
            else if (Keyboard.FocusedElement is TextBox focused && IsAncestorOf(focused)) Focus();
            // Keep the entire entry (including quota/upgrade state) inside the viewport.
            editor.BringIntoView();
            TaskScrollViewer.UpdateLayout();
            var top = editor.TranslatePoint(new Point(), TaskScrollViewer).Y;
            var bottom = top + editor.ActualHeight;
            if (top < 0) TaskScrollViewer.ScrollToVerticalOffset(TaskScrollViewer.VerticalOffset + top);
            else if (bottom > TaskScrollViewer.ViewportHeight)
                TaskScrollViewer.ScrollToVerticalOffset(TaskScrollViewer.VerticalOffset + bottom - TaskScrollViewer.ViewportHeight);
            TaskScrollViewer.UpdateLayout();
            _creationNeedsFocus = _creationModel.CanCreateSubTask && input?.IsKeyboardFocused != true;
            return;
        }
        if (input is null || !input.IsVisible || !input.IsEnabled) return;
        input.BringIntoView();
        input.Focus();
        Keyboard.Focus(input);
        _creationNeedsFocus = !input.IsKeyboardFocused;
        DesktopDiagnostics.RecordAction("Floating.EditorFocusEnd", $"Focused={input.IsKeyboardFocused}");
    }

    private TextBox? CreationInput() => _creationModel is { IsCreatingSubTask: true, SubTaskCreationParent: { } parent }
        ? TaskRows().Where(row => ReferenceEquals(row.DataContext, parent))
            .Select(row => FindCompletionElement<TextBox>(row, "InlineSubTaskTitleInput")).FirstOrDefault()
        : TaskTitleInput;

    private void CreationStateChanged(object? sender, EventArgs e)
    {
        if (_creationModel is null) return;
        DesktopDiagnostics.RecordAction("Floating.EditorState", $"Creating={_creationModel.IsCreatingTask}; Loaded={IsLoaded}; Visible={IsVisible}");
        _creationFromHeight = _creationModel.CreationHeight;
        _creationFromTaskArea = _creationModel.TaskAreaHeight;
        _creationToHeight = _creationModel.IsCreatingRootTask ? _creationModel.RootTaskCreationHeight : 0;
        var inline = _creationModel.IsCreatingSubTask || _inlineCreationPresentation;
        _inlineCreationPresentation = _creationModel.IsCreatingSubTask;
        StopCreationTimer();
        if (inline || !IsLoaded || !IsVisible || !CreationAnimationPreference())
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
            if (TaskTitleInput.IsKeyboardFocusWithin || Keyboard.FocusedElement is TextBox { Name: "InlineSubTaskTitleInput" } input && IsAncestorOf(input)) Focus();
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
        Dispatcher.Invoke(() => { StopCreationTimer(); _creationModel?.SetCreationPresentation(_creationModel.IsCreatingRootTask ? _creationModel.RootTaskCreationHeight : 0); });
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
