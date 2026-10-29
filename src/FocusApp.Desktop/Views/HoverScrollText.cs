using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FocusApp.Desktop.Views;

/// <summary>Keeps a single-line title bounded, with a tooltip or scrolling to reveal overflow.</summary>
public sealed class HoverScrollText : Decorator
{
    public static readonly DependencyProperty IsHoveredProperty = DependencyProperty.Register(
        nameof(IsHovered), typeof(bool), typeof(HoverScrollText), new PropertyMetadata(false, HoverChanged));
    public static readonly DependencyProperty UseOverflowToolTipProperty = DependencyProperty.Register(
        nameof(UseOverflowToolTip), typeof(bool), typeof(HoverScrollText), new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.AffectsArrange, HoverChanged));

    private readonly TranslateTransform _offset = new();
    private readonly RectangleGeometry _viewport = new();
    private TextBlock? _title;
    private Transform? _originalTransform;
    private TransformGroup? _scrollTransform;
    private TextTrimming _originalTrimming;
    private DispatcherOperation? _pendingMotion;
    private double _naturalWidth;
    private double _motionTarget;
    private bool _revealing;
    private int _motionVersion;

    public bool IsHovered { get => (bool)GetValue(IsHoveredProperty); set => SetValue(IsHoveredProperty, value); }
    public bool UseOverflowToolTip { get => (bool)GetValue(UseOverflowToolTipProperty); set => SetValue(UseOverflowToolTipProperty, value); }
    internal double Offset => _offset.X;
    internal double OverflowDistance { get; private set; }
    internal Func<bool> AnimationPreference { get; set; } = () => SystemParameters.ClientAreaAnimation;

    public HoverScrollText()
    {
        ClipToBounds = true;
        Clip = _viewport;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueMotion(); else Reset(); };
        DataContextChanged += (_, _) => { Reset(); QueueMotion(); };
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        // Measure with the same native TextBlock/font, then constrain the viewport as before.
        Child.Measure(new Size(double.PositiveInfinity, constraint.Height));
        _naturalWidth = Child.DesiredSize.Width;
        Child.Measure(constraint);
        return new Size(Math.Min(constraint.Width, Child.DesiredSize.Width), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var previousOverflow = OverflowDistance;
        OverflowDistance = Math.Max(0, _naturalWidth - arrangeSize.Width);
        Child?.Arrange(new Rect(0, 0, _revealing ? Math.Max(arrangeSize.Width, _naturalWidth) : arrangeSize.Width, arrangeSize.Height));
        // Honor any padding reserved by the title without expanding its visible viewport.
        var padding = (Child as TextBlock)?.Padding ?? default;
        _viewport.Rect = new Rect(padding.Left, 0,
            Math.Max(0, arrangeSize.Width - padding.Left - padding.Right), arrangeSize.Height);
        if (UseOverflowToolTip)
        {
            var truncated = OverflowDistance > .5;
            var text = truncated ? (Child as TextBlock)?.Text : null;
            if (!Equals(ToolTip, text)) SetCurrentValue(ToolTipProperty, text);
            ToolTipService.SetIsEnabled(this, truncated);
        }
        if (Math.Abs(previousOverflow - OverflowDistance) > .1) QueueMotion();
        return arrangeSize;
    }

    private static void HoverChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((HoverScrollText)sender).UpdateMotion();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_title is not null || Child is not TextBlock title) return;
        _title = title;
        _originalTrimming = title.TextTrimming;
        _originalTransform = title.RenderTransform;
        _scrollTransform = new TransformGroup();
        _scrollTransform.Children.Add(_originalTransform);
        _scrollTransform.Children.Add(_offset);
        title.SetCurrentValue(RenderTransformProperty, _scrollTransform);
        SystemParameters.StaticPropertyChanged += AnimationSettingsChanged;
        QueueMotion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= AnimationSettingsChanged;
        Reset();
        if (_title is not null && ReferenceEquals(_title.RenderTransform, _scrollTransform))
            _title.SetCurrentValue(RenderTransformProperty, _originalTransform);
        _title = null;
        _originalTransform = null;
        _scrollTransform = null;
    }

    private void AnimationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) QueueMotion();
    }

    private void QueueMotion()
    {
        if (!IsLoaded || _pendingMotion is { Status: DispatcherOperationStatus.Pending }) return;
        _pendingMotion = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _pendingMotion = null;
            UpdateMotion();
        }));
    }

    private void UpdateMotion()
    {
        if (UseOverflowToolTip) { Reset(); return; }
        if (_title is null || !IsLoaded || !IsVisible || ActualWidth <= 0) return;
        if (OverflowDistance <= .5)
        {
            Reset();
            return;
        }

        var reveal = IsHovered;
        var target = reveal ? -OverflowDistance : 0;
        if (!_revealing && !reveal) return;
        if (_revealing && Math.Abs(_motionTarget - target) < .1 && AnimationPreference()) return;
        var from = _offset.X;
        var version = ++_motionVersion;
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        _offset.X = from;
        _motionTarget = target;

        if (reveal)
        {
            _revealing = true;
            _title.SetCurrentValue(TextBlock.TextTrimmingProperty, TextTrimming.None);
            InvalidateMeasure();
        }

        if (!AnimationPreference() || Math.Abs(from - target) < .1)
        {
            _offset.X = target;
            if (!reveal) Reset();
            return;
        }

        var duration = reveal
            ? Math.Max(.6, Math.Abs(target - from) / 40)
            : Math.Clamp(Math.Abs(from) / 600, .18, .45);
        var animation = new DoubleAnimation(from, target, TimeSpan.FromSeconds(duration))
        {
            BeginTime = reveal && Math.Abs(from) < .1 ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero,
            EasingFunction = new SineEase { EasingMode = reveal ? EasingMode.EaseInOut : EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        if (!reveal) animation.Completed += (_, _) =>
        {
            if (version == _motionVersion && !IsHovered) Reset();
        };
        _offset.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void Reset()
    {
        _pendingMotion?.Abort();
        _pendingMotion = null;
        _motionVersion++;
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        _offset.X = 0;
        _motionTarget = 0;
        if (!_revealing) return;
        _revealing = false;
        _title?.SetCurrentValue(TextBlock.TextTrimmingProperty, _originalTrimming);
        InvalidateMeasure();
    }
}
