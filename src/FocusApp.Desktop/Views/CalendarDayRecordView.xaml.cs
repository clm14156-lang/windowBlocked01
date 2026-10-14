using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class CalendarDayRecordView : UserControl
{
    private static readonly Duration ExpansionDuration = new(TimeSpan.FromMilliseconds(180));
    private CalendarDayRecordViewModel? _entry;
    private int _animationVersion;

    public CalendarDayRecordView()
    {
        InitializeComponent();
        Loaded += (_, _) => { AttachEntry(); UpdateExpansion(false); };
        Unloaded += (_, _) => { DetachEntry(); StopAnimations(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopAnimations(); else if (IsLoaded) UpdateExpansion(false); };
        DataContextChanged += (_, _) => { DetachEntry(); if (IsLoaded) AttachEntry(); UpdateExpansion(false); };
        SizeChanged += (_, e) => { if (e.WidthChanged && _entry?.IsExpanded == true) UpdateExpansion(false); };
    }

    private void AttachEntry()
    {
        if (ReferenceEquals(_entry, DataContext)) return;
        _entry = DataContext as CalendarDayRecordViewModel;
        if (_entry is not null) _entry.PropertyChanged += EntryChanged;
    }
    private void DetachEntry()
    {
        if (_entry is not null) _entry.PropertyChanged -= EntryChanged;
        _entry = null;
    }
    private void EntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarDayRecordViewModel.IsExpanded)) UpdateExpansion(true);
    }
    private void StopAnimations()
    {
        _animationVersion++;
        TaskExpansion.BeginAnimation(HeightProperty, null);
        TaskExpansion.BeginAnimation(OpacityProperty, null);
        ArrowRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
    }
    private void UpdateExpansion(bool animate)
    {
        var expanded = (DataContext as CalendarDayRecordViewModel)?.IsExpanded == true;
        TaskItems.Measure(new Size(Math.Max(1, ActualWidth - 42), double.PositiveInfinity));
        var target = expanded ? TaskItems.DesiredSize.Height : 0;
        var currentHeight = TaskExpansion.ActualHeight;
        var currentOpacity = TaskExpansion.Opacity;
        var currentAngle = ArrowRotation.Angle;
        StopAnimations();
        TaskExpansion.Height = target;
        TaskExpansion.Opacity = expanded ? 1 : 0;
        ArrowRotation.Angle = expanded ? 90 : 0;
        if (!animate || !IsVisible || !SystemParameters.ClientAreaAnimation) return;
        var version = _animationVersion;
        var height = CreateAnimation(currentHeight, target);
        height.Completed += (_, _) => { if (version == _animationVersion) StopAnimations(); };
        TaskExpansion.BeginAnimation(HeightProperty, height);
        TaskExpansion.BeginAnimation(OpacityProperty, CreateAnimation(currentOpacity, expanded ? 1 : 0));
        ArrowRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, CreateAnimation(currentAngle, expanded ? 90 : 0));
    }
    private static DoubleAnimation CreateAnimation(double from, double to) => new(from, to, ExpansionDuration)
    {
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop
    };
}
