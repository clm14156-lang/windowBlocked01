using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FocusApp.Desktop.ViewModels;
using static FocusApp.Desktop.Views.TaskCompletionMotion;

namespace FocusApp.Desktop.Views;

public partial class FocusTaskDrawer
{
    private FocusTaskDrawerViewModel? _presentationModel;
    private readonly Dictionary<FocusTaskViewModel, CompletionAnimation> _completionAnimations = [];
    private readonly Dictionary<FrameworkElement, SubTaskFeedback> _subTaskFeedback = [];
    private Storyboard? _progressCollapse;
    private object _progressClipToBounds = DependencyProperty.UnsetValue;

    private void Drawer_Loaded(object sender, RoutedEventArgs e) => AttachCompletionPresentation();

    private void Drawer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) _presentationModel?.FinishCompletionAnimations();
    }

    private void AttachCompletionPresentation()
    {
        if (ReferenceEquals(_presentationModel, ViewModel)) return;
        _presentationModel = ViewModel;
        if (_presentationModel is null) return;
        _presentationModel.CompletionAnimationRequested += CompletionAnimationRequested;
        _presentationModel.CompletionAnimationCancelled += CompletionAnimationCancelled;
        _presentationModel.PropertyChanged += PresentationModel_PropertyChanged;
    }

    private void DetachCompletionPresentation()
    {
        if (_presentationModel is { } model)
        {
            model.FinishCompletionAnimations();
            model.CompletionAnimationRequested -= CompletionAnimationRequested;
            model.CompletionAnimationCancelled -= CompletionAnimationCancelled;
            model.PropertyChanged -= PresentationModel_PropertyChanged;
        }
        _presentationModel = null;
        foreach (var animation in _completionAnimations.Values.ToArray()) animation.Reset(this);
        _completionAnimations.Clear();
        foreach (var row in _subTaskFeedback.Keys.ToArray()) DetachSubTaskFeedback(row);
        StopProgressCollapse();
    }

    private void PresentationModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusTaskDrawerViewModel.HasPendingTasks)) return;
        if (_presentationModel?.Tasks.Any(task => !task.IsCompleted) == true) StopProgressCollapse();
        else if (_completionAnimations.Count > 0) StartProgressCollapse();
    }

    private void CompletionAnimationRequested(object? sender, TaskCompletionPresentationEventArgs e)
    {
        if (!IsLoaded || !IsVisible || !e.Task.IsCompleted) return;
        if (_completionAnimations.ContainsKey(e.Task)) { e.Handled = true; return; }
        var row = GetTaskRows().FirstOrDefault(item => ReferenceEquals(item.DataContext, e.Task));
        if (row is null || row.ActualHeight <= 0 ||
            FindCompletionElement<CheckBox>(row, "TaskCompletionCheck") is not { } check ||
            FindCompletionElement<TextBlock>(row, "TaskTitle") is not { } title) return;

        if (ReferenceEquals(_dragTask, e.Task)) ResetTaskDrag();
        check.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
        var animation = CreateCompletionFeedback(check, title, parent: true);
        _completionAnimations.Add(e.Task, animation);
        animation.Set(row, ClipToBoundsProperty, true);
        var translation = new TranslateTransform();
        animation.Set(row, RenderTransformProperty, translation);
        AddCompletionKeys(animation.Storyboard, row, OpacityProperty, (0, 1), (350, 1), (500, 0));
        AddCompletionKeys(animation.Storyboard, row, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.YProperty), (0, 0), (350, 0), (500, -3));
        AddCompletionKeys(animation.Storyboard, row, HeightProperty, (0, row.ActualHeight), (400, row.ActualHeight), (600, 0));
        animation.Storyboard.Duration = TimeSpan.FromMilliseconds(600);
        var owner = _presentationModel;
        animation.Storyboard.Completed += (_, _) =>
        {
            if (!_completionAnimations.TryGetValue(e.Task, out var current) || !ReferenceEquals(current, animation)) return;
            _completionAnimations.Remove(e.Task);
            animation.Reset(this);
            owner?.FinishCompletionAnimation(e.Task);
            if (_completionAnimations.Count == 0) StopProgressCollapse();
        };
        animation.Storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        e.Handled = true;
    }

    private void CompletionAnimationCancelled(object? sender, FocusTaskViewModel task)
    {
        if (_completionAnimations.Remove(task, out var animation)) animation.Reset(this);
        StopProgressCollapse();
    }

    private void StartProgressCollapse()
    {
        if (_progressCollapse is not null || DrawerTaskProgress.ActualHeight <= 0) return;
        _progressClipToBounds = DrawerTaskProgress.ReadLocalValue(ClipToBoundsProperty);
        DrawerTaskProgress.ClipToBounds = true;
        _progressCollapse = new Storyboard { Duration = TimeSpan.FromMilliseconds(600) };
        AddCompletionKeys(_progressCollapse, DrawerTaskProgress, OpacityProperty, (0, 1), (350, 1), (500, 0));
        AddCompletionKeys(_progressCollapse, DrawerTaskProgress, HeightProperty,
            (0, DrawerTaskProgress.ActualHeight), (400, DrawerTaskProgress.ActualHeight), (600, 0));
        var margin = new ThicknessAnimation(DrawerTaskProgress.Margin,
            new Thickness(DrawerTaskProgress.Margin.Left, 0, DrawerTaskProgress.Margin.Right, 0), TimeSpan.FromMilliseconds(200))
        { BeginTime = TimeSpan.FromMilliseconds(400), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(margin, DrawerTaskProgress);
        Storyboard.SetTargetProperty(margin, new PropertyPath(MarginProperty));
        _progressCollapse.Children.Add(margin);
        _progressCollapse.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void StopProgressCollapse()
    {
        if (_progressCollapse is null) return;
        _progressCollapse?.Remove(this);
        _progressCollapse = null;
        if (_progressClipToBounds == DependencyProperty.UnsetValue) DrawerTaskProgress.ClearValue(ClipToBoundsProperty);
        else DrawerTaskProgress.SetValue(ClipToBoundsProperty, _progressClipToBounds);
        _progressClipToBounds = DependencyProperty.UnsetValue;
    }

    private void SubTaskRow_Loaded(object sender, RoutedEventArgs e) => AttachSubTaskFeedback((FrameworkElement)sender);
    private void SubTaskRow_Unloaded(object sender, RoutedEventArgs e) => DetachSubTaskFeedback((FrameworkElement)sender);
    private void SubTaskRow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var row = (FrameworkElement)sender;
        DetachSubTaskFeedback(row);
        if (row.IsLoaded) AttachSubTaskFeedback(row);
    }

    private void AttachSubTaskFeedback(FrameworkElement row)
    {
        if (_subTaskFeedback.ContainsKey(row) || row.DataContext is not FocusSubTaskViewModel model) return;
        var feedback = new SubTaskFeedback(model);
        feedback.Handler = (_, args) =>
        {
            if (args.PropertyName != nameof(FocusSubTaskViewModel.IsCompleted)) return;
            feedback.Animation?.Reset(this);
            feedback.Animation = null;
            if (!model.IsCompleted || !row.IsVisible ||
                FindCompletionElement<CheckBox>(row, "SubTaskCompletionCheck") is not { } check ||
                FindCompletionElement<TextBlock>(row, "SubTaskTitle") is not { } title) return;
            check.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
            var animation = CreateCompletionFeedback(check, title, parent: false);
            feedback.Animation = animation;
            animation.Storyboard.Duration = TimeSpan.FromMilliseconds(250);
            animation.Storyboard.Completed += (_, _) =>
            {
                if (!ReferenceEquals(feedback.Animation, animation)) return;
                feedback.Animation = null;
                animation.Reset(this);
            };
            animation.Storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        };
        _subTaskFeedback.Add(row, feedback);
        model.PropertyChanged += feedback.Handler;
    }

    private void DetachSubTaskFeedback(FrameworkElement row)
    {
        if (!_subTaskFeedback.Remove(row, out var feedback)) return;
        feedback.Model.PropertyChanged -= feedback.Handler;
        feedback.Animation?.Reset(this);
    }


    private sealed class SubTaskFeedback(FocusSubTaskViewModel model)
    {
        public FocusSubTaskViewModel Model { get; } = model;
        public PropertyChangedEventHandler? Handler { get; set; }
        public CompletionAnimation? Animation { get; set; }
    }
}
