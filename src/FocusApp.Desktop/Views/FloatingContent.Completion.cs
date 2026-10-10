using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FocusApp.Desktop.ViewModels;
using static FocusApp.Desktop.Views.TaskCompletionMotion;

namespace FocusApp.Desktop.Views;

public partial class FloatingContent
{
    private static readonly DependencyProperty CompletionHeaderHeightProperty = DependencyProperty.Register(
        "CompletionHeaderHeight", typeof(double), typeof(FloatingContent), new PropertyMetadata(FocusFloatingWindowViewModel.TaskHeaderHeight));
    private FocusFloatingWindowViewModel? _completionModel;
    private readonly Dictionary<string, CompletionAnimation> _parentAnimations = [];
    private readonly Dictionary<FrameworkElement, ChildFeedback> _childAnimations = [];
    private CompletionAnimation? _headerAnimation;
    private bool _renderingSubscribed;
    internal Func<bool> CompletionAnimationPreference { get; set; } = () => SystemParameters.ClientAreaAnimation;
    private Color FloatingAccentColor => ((SolidColorBrush)FindResource("AccentPrimary")).Color;

    private void Content_Loaded(object sender, RoutedEventArgs e) => AttachCompletionPresentation();
    private void Content_Unloaded(object sender, RoutedEventArgs e) => DetachCompletionPresentation();
    private void Content_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachCompletionPresentation();
        if (IsLoaded) AttachCompletionPresentation();
    }
    private void Content_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) FinishFeedback();
    }
    private void AttachCompletionPresentation()
    {
        if (ReferenceEquals(_completionModel, DataContext)) return;
        _completionModel = DataContext as FocusFloatingWindowViewModel;
        if (_completionModel is null) return;
        _completionModel.CompletionAnimationRequested += ParentCompletionRequested;
        _completionModel.CompletionAnimationCancelled += ParentCompletionCancelled;
        _completionModel.PropertyChanged += CompletionModel_PropertyChanged;
        SystemParameters.StaticPropertyChanged += AnimationSettingsChanged;
    }
    private void DetachCompletionPresentation()
    {
        FinishFeedback();
        if (_completionModel is { } model)
        {
            model.CompletionAnimationRequested -= ParentCompletionRequested;
            model.CompletionAnimationCancelled -= ParentCompletionCancelled;
            model.PropertyChanged -= CompletionModel_PropertyChanged;
            SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged;
        }
        _completionModel = null;
        foreach (var row in _childAnimations.Keys.ToArray()) DetachChild(row);
    }
    private void FinishFeedback()
    {
        _completionModel?.FinishCompletionAnimations();
        foreach (var animation in _parentAnimations.Values) animation.Reset(this);
        _parentAnimations.Clear();
        foreach (var feedback in _childAnimations.Values) { feedback.Animation?.Reset(this); feedback.Animation = null; }
        StopHeaderCollapse();
        StopRendering();
    }
    private void AnimationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !CompletionAnimationPreference())
            Dispatcher.Invoke(FinishFeedback);
    }
    private void CompletionModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FocusFloatingWindowViewModel.HasDisplayedTasks)) return;
        if (_completionModel?.PendingTaskCount > 0) StopHeaderCollapse();
        else if (_parentAnimations.Count > 0) StartHeaderCollapse();
    }
    private void ParentCompletionRequested(object? sender, TaskCompletionPresentationEventArgs e)
    {
        if (!IsLoaded || !IsVisible || !CompletionAnimationPreference() || !e.Task.IsCompleted ||
            e.Task.SubTasks.Any(child => !child.IsCompleted)) return;
        if (_parentAnimations.ContainsKey(e.Task.TaskId)) { e.Handled = true; return; }
        var row = TaskRows().FirstOrDefault(item => item.DataContext is FocusTaskViewModel task && task.TaskId == e.Task.TaskId);
        if (row is null || row.ActualHeight <= 0 ||
            FindCompletionElement<CheckBox>(row, "ParentTaskCheck") is not { } check ||
            FindCompletionElement<TextBlock>(row, "ParentTaskTitle") is not { } title) return;
        check.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
        var animation = CreateCompletionFeedback(check, title, parent: true,
            Color.FromRgb(0x3C, 0x48, 0x5B), Color.FromRgb(0x7D, 0x8C, 0xA2), accentColor: FloatingAccentColor);
        _parentAnimations.Add(e.Task.TaskId, animation);
        // The business state is already complete; ignore further input during its visual exit.
        animation.Set(row, IsEnabledProperty, false);
        animation.Set(row, RenderTransformProperty, new TranslateTransform());
        AddCompletionKeys(animation.Storyboard, row, OpacityProperty, (0, 1), (350, 1), (500, 0));
        AddCompletionKeys(animation.Storyboard, row, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.YProperty),
            (0, 0), (350, 0), (500, -3));
        AddCompletionKeys(animation.Storyboard, row, HeightProperty, (0, row.ActualHeight), (400, row.ActualHeight), (550, 0));
        animation.Storyboard.Duration = TimeSpan.FromMilliseconds(550);
        var model = _completionModel;
        animation.Storyboard.Completed += (_, _) =>
        {
            if (!_parentAnimations.TryGetValue(e.Task.TaskId, out var current) || !ReferenceEquals(current, animation)) return;
            _parentAnimations.Remove(e.Task.TaskId);
            animation.Reset(this);
            model?.FinishCompletionAnimation(e.Task.TaskId);
            if (_parentAnimations.Count == 0) { StopHeaderCollapse(); StopRendering(); }
        };
        animation.Storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        if (!_renderingSubscribed) { CompositionTarget.Rendering += CompletionFrame; _renderingSubscribed = true; }
        e.Handled = true;
    }
    private void ParentCompletionCancelled(object? sender, FocusTaskViewModel task)
    {
        if (_parentAnimations.Remove(task.TaskId, out var animation)) animation.Reset(this);
        StopHeaderCollapse();
        if (_parentAnimations.Count == 0) StopRendering();
    }
    private void CompletionFrame(object? sender, EventArgs e)
    {
        if (_completionModel is null || _parentAnimations.Count == 0) return;
        UpdateLayout();
        var rowsHeight = TaskRows().Sum(row => double.IsNaN(row.Height) ? row.ActualHeight : row.Height);
        _completionModel.SetCompletionPresentationHeight(rowsHeight, (double)GetValue(CompletionHeaderHeightProperty));
    }
    private void StopRendering()
    {
        if (!_renderingSubscribed) return;
        CompositionTarget.Rendering -= CompletionFrame;
        _renderingSubscribed = false;
    }
    private void StartHeaderCollapse()
    {
        if (_headerAnimation is not null) return;
        _headerAnimation = new CompletionAnimation();
        AddCompletionKeys(_headerAnimation.Storyboard, this, CompletionHeaderHeightProperty, (0, 46), (400, 46), (550, 0));
        AddCompletionKeys(_headerAnimation.Storyboard, TaskDisplayBorder, OpacityProperty, (0, 1), (350, 1), (500, 0));
        _headerAnimation.Storyboard.Duration = TimeSpan.FromMilliseconds(550);
        _headerAnimation.Storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }
    private void StopHeaderCollapse()
    {
        _headerAnimation?.Reset(this);
        _headerAnimation = null;
    }
    private IEnumerable<Grid> TaskRows() => Descendants<Grid>(PendingTaskList).Where(row => row.Name == "FloatingTaskRow");
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private void ChildRow_Loaded(object sender, RoutedEventArgs e) => AttachChild((FrameworkElement)sender);
    private void ChildRow_Unloaded(object sender, RoutedEventArgs e) => DetachChild((FrameworkElement)sender);
    private void ChildRow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var row = (FrameworkElement)sender;
        DetachChild(row);
        if (row.IsLoaded) AttachChild(row);
    }
    private void AttachChild(FrameworkElement row)
    {
        if (_childAnimations.ContainsKey(row) || row.DataContext is not FocusSubTaskViewModel child) return;
        var feedback = new ChildFeedback(child);
        feedback.Handler = (_, args) =>
        {
            if (args.PropertyName != nameof(child.IsCompleted)) return;
            feedback.Animation?.Reset(this); feedback.Animation = null;
            if (!child.IsCompleted || !row.IsVisible || !CompletionAnimationPreference() ||
                FindCompletionElement<CheckBox>(row, "SubTaskCheck") is not { } check ||
                FindCompletionElement<TextBlock>(row, "SubTaskTitle") is not { } title) return;
            check.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
            var animation = CreateCompletionFeedback(check, title, parent: false,
                Color.FromRgb(0x3C, 0x48, 0x5B), Color.FromRgb(0x7D, 0x8C, 0xA2), accentChild: true, accentColor: FloatingAccentColor);
            feedback.Animation = animation;
            animation.Storyboard.Duration = TimeSpan.FromMilliseconds(250);
            animation.Storyboard.Completed += (_, _) =>
            {
                if (!ReferenceEquals(feedback.Animation, animation)) return;
                animation.Reset(this); feedback.Animation = null;
            };
            animation.Storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        };
        _childAnimations.Add(row, feedback);
        child.PropertyChanged += feedback.Handler;
    }
    private void DetachChild(FrameworkElement row)
    {
        if (!_childAnimations.Remove(row, out var feedback)) return;
        feedback.Model.PropertyChanged -= feedback.Handler;
        feedback.Animation?.Reset(this);
    }
    private sealed class ChildFeedback(FocusSubTaskViewModel model)
    {
        public FocusSubTaskViewModel Model { get; } = model;
        public PropertyChangedEventHandler? Handler { get; set; }
        public CompletionAnimation? Animation { get; set; }
    }
}
