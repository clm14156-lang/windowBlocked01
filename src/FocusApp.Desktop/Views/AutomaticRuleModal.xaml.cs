using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusApp.Core;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

public partial class AutomaticRuleModal : UserControl
{
    // WPF device-independent units: one minute per DIP, plus room for endpoint labels.
    private const double TopInset = 12, LabelWidth = 60, ResizeHandleHeight = 6;
    private const string DefaultHint = "拖拽空白创建 · 单击编辑 · 拖动中间移动 · 拖动边缘调整";
    private enum RuleDragMode { Move, ResizeStart, ResizeEnd }
    private AutomaticRuleItemViewModel? _pressedRule;
    private Point _pressPoint;
    private bool _moving;
    private RuleDragMode _dragMode;
    private double? _anchor;
    private (double Start, double End)? _draft;
    private Border? _draftBlock;
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private AutomaticRuleModalViewModel? _subscribed;
    private AutomaticRuleEditorWindow? _editorWindow;
    private AutomaticRuleModalViewModel? Model => DataContext as AutomaticRuleModalViewModel;

    public AutomaticRuleModal()
    {
        InitializeComponent();
        _scrollTimer.Tick += (_, _) => AutoScroll();
        DataContextChanged += (_, _) => Subscribe();
    }
    private void OnLoaded(object sender, RoutedEventArgs e) => Subscribe();
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CloseEditorWindow();
        CancelDrag();
        if (_subscribed is not null) _subscribed.PropertyChanged -= ModelChanged;
        _subscribed = null;
    }
    private void Subscribe()
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= ModelChanged;
        _subscribed = Model;
        if (_subscribed is not null) _subscribed.PropertyChanged += ModelChanged;
        RenderTimeline();
        if (IsLoaded && Model?.IsEditorOpen == true) ShowEditorWindow();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutomaticRuleModalViewModel.GetRules)
            or nameof(AutomaticRuleModalViewModel.SelectedRuleId)) RenderTimeline();
        if (e.PropertyName == nameof(AutomaticRuleModalViewModel.IsOpen))
        {
            CancelDrag();
            if (Model?.IsOpen == true) { TimelineScroll.ScrollToTop(); RenderTimeline(); }
            else CloseEditorWindow();
        }
        if (e.PropertyName == nameof(AutomaticRuleModalViewModel.IsEditorOpen))
        {
            if (Model?.IsEditorOpen == true) ShowEditorWindow();
            else
            {
                CloseEditorWindow();
                _draft = null;
                RenderTimeline();
            }
        }
    }
    private void ShowEditorWindow()
    {
        if (!IsLoaded || Model?.IsEditorOpen != true) return;
        if (_editorWindow is not null)
        {
            PositionEditorWindow(_editorWindow);
            return;
        }

        var editor = new AutomaticRuleEditorWindow(Model);
        var owner = Window.GetWindow(this);
        if (owner is not null) editor.Owner = owner;
        editor.Closed += (_, _) => { if (ReferenceEquals(_editorWindow, editor)) _editorWindow = null; };
        _editorWindow = editor;
        PositionEditorWindow(editor);
        editor.Show();
        editor.Activate();
    }
    private void PositionEditorWindow(AutomaticRuleEditorWindow editor)
    {
        if (!IsLoaded) return;
        TimelineScroll.ApplyTemplate();
        var scrollBar = TimelineScroll.Template.FindName("PART_VerticalScrollBar", TimelineScroll) as FrameworkElement;
        var editorLeft = scrollBar is not null
            ? scrollBar.TranslatePoint(new Point(), this).X - AutomaticRuleEditorWindow.SurfaceInset
            : TimelineScroll.TranslatePoint(new Point(TimelineScroll.ActualWidth, 0), this).X
              - AutomaticRuleEditorWindow.SurfaceInset;
        var blockTop = Timeline.TranslatePoint(new Point(0, TopInset + (Model?.StartValue ?? 0)), this).Y;
        var screenPoint = PointToScreen(new Point(editorLeft, blockTop));
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
        editor.Left = screenPoint.X;
        editor.Top = screenPoint.Y;
    }
    private void CloseEditorWindow()
    {
        var editor = _editorWindow;
        _editorWindow = null;
        if (editor?.IsLoaded == true) editor.Close();
    }
    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTimeline();
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private Brush ResourceBrush(string key, string fallback)
        => TryFindResource(key) as Brush ?? Brush(fallback);
    private void RenderTimeline()
    {
        if (Timeline is null) return;
        Timeline.Children.Clear();
        var width = Math.Max(0, Timeline.ActualWidth - LabelWidth - 8);
        for (var hour = 0; hour <= 24; hour++)
        {
            var y = TopInset + hour * 60;
            var label = new TextBlock
            {
                Text = $"{hour:00}:00",
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Foreground = ResourceBrush("TextWeak", "#8E8E93"),
                IsHitTestVisible = false
            };
            Canvas.SetTop(label, y - 7); Timeline.Children.Add(label);
            var line = new Line { X1 = LabelWidth, X2 = LabelWidth + width, Y1 = y, Y2 = y, Stroke = Brush("#EDEDF1"), StrokeThickness = 1, IsHitTestVisible = false };
            Timeline.Children.Add(line);
        }
        if (Model is null) return;
        foreach (var rule in Model.GetRules())
        foreach (var segment in RuleTimelineRange.Segments(rule.StartMinutes, rule.EndMinutes))
        {
            if (_moving && _draft is not null && rule.Id == _pressedRule?.Id) continue;
            var block = MakeBlock(
                segment.Start,
                segment.End,
                false,
                RuleTargetName(rule),
                RuleCustomPeriod(rule),
                rule.Id == Model.SelectedRuleId);
            block.PreviewMouseLeftButtonDown += (_, e) => BeginRuleInteraction(
                rule,
                (e.OriginalSource as FrameworkElement)?.Tag is RuleDragMode mode ? mode : RuleDragMode.Move,
                e);
            block.Tag = rule.Id;
            Timeline.Children.Add(block);
        }
        _draftBlock = null;
        DrawDraft();
        RequestSelectedRuleScroll();
    }

    private void RequestSelectedRuleScroll()
    {
        var selectedRuleId = Model?.SelectedRuleId;
        if (!IsLoaded || selectedRuleId is null || _moving) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (Model?.SelectedRuleId != selectedRuleId || !IsLoaded) return;
            ScrollSelectedRuleIntoView(selectedRuleId.Value);
        }));
    }

    private void ScrollSelectedRuleIntoView(Guid selectedRuleId)
    {
        TimelineScroll.UpdateLayout();
        var block = Timeline.Children.OfType<Border>()
            .FirstOrDefault(candidate => candidate.Tag is Guid ruleId && ruleId == selectedRuleId);
        if (block is null || TimelineScroll.ViewportHeight <= 0) return;

        var blockTop = Canvas.GetTop(block);
        var blockBottom = blockTop + block.ActualHeight;
        var viewportTop = TimelineScroll.VerticalOffset;
        var viewportBottom = viewportTop + TimelineScroll.ViewportHeight;
        if (blockTop >= viewportTop && blockBottom <= viewportBottom)
        {
            PositionEditorWindowIfOpen();
            return;
        }

        var centeredOffset = blockTop + block.ActualHeight / 2 - TimelineScroll.ViewportHeight / 2;
        TimelineScroll.ScrollToVerticalOffset(Math.Clamp(centeredOffset, 0, TimelineScroll.ScrollableHeight));
        TimelineScroll.UpdateLayout();
        PositionEditorWindowIfOpen();
    }

    private void PositionEditorWindowIfOpen()
    {
        if (_editorWindow?.IsLoaded == true) PositionEditorWindow(_editorWindow);
    }
    private string? RuleTargetName(AutomaticRuleItemViewModel rule)
        => string.IsNullOrEmpty(rule.TargetId)
            ? null
            : Model?.Targets.FirstOrDefault(target => target.Id == rule.TargetId)?.Name;
    private static string? RuleCustomPeriod(AutomaticRuleItemViewModel rule)
    {
        if (!rule.IsCustom) return null;
        var days = rule.DayKeys
            .Select(key => (Order: WeekdayOrder(key), Name: WeekdayName(key)))
            .Where(day => day.Order >= 0)
            .OrderBy(day => day.Order)
            .Select(day => day.Name)
            .ToArray();
        return days.Length == 0 ? null : $"（{string.Join("、", days)}）";
    }
    private static int WeekdayOrder(string key) => key switch
    {
        "Monday" => 0, "Tuesday" => 1, "Wednesday" => 2, "Thursday" => 3,
        "Friday" => 4, "Saturday" => 5, "Sunday" => 6, _ => -1
    };
    private static string WeekdayName(string key) => key switch
    {
        "Monday" => "周一", "Tuesday" => "周二", "Wednesday" => "周三", "Thursday" => "周四",
        "Friday" => "周五", "Saturday" => "周六", "Sunday" => "周日", _ => string.Empty
    };
    private Border MakeBlock(
        double start,
        double end,
        bool draft,
        string? targetName,
        string? customPeriod = null,
        bool selected = false)
    {
        var content = new Grid();
        content.Children.Add(MakeBlockText(
            start,
            end,
            draft && _pressedRule is null,
            targetName,
            customPeriod,
            draft || selected ? "#BE5C00" : "#6E6E73"));
        if (!draft)
        {
            content.Children.Add(MakeResizeHandle(RuleDragMode.ResizeStart, VerticalAlignment.Top));
            content.Children.Add(MakeResizeHandle(RuleDragMode.ResizeEnd, VerticalAlignment.Bottom));
        }

        var block = new Border
        {
            Width = Math.Max(0, Timeline.ActualWidth - LabelWidth - 8), Height = Math.Max(1, end - start),
            Background = Brush(draft || selected ? "#FFF0DF" : "#EEEEF1"), BorderBrush = Brush(draft || selected ? "#FF8000" : "#CCCCD3"),
            BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand, ClipToBounds = true,
            Child = content
        };
        if (draft && _moving && _pressedRule is not null)
        {
            block.Effect = new DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 2,
                Direction = 270,
                Opacity = 0.22,
                Color = Colors.Black
            };
            Panel.SetZIndex(block, 1000);
        }
        Canvas.SetLeft(block, LabelWidth); Canvas.SetTop(block, TopInset + start);
        return block;
    }
    private FrameworkElement MakeBlockText(
        double start,
        double end,
        bool isCreating,
        string? targetName,
        string? customPeriod,
        string foreground)
    {
        var margin = new Thickness(8, end - start >= 30 ? 6 : 0, 6, 0);
        var timeRange = $"{Time(start)}–{Time(end)}";
        if (isCreating)
        {
            return new TextBlock
            {
                Text = $"{timeRange} · {Duration(end - start)}",
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Foreground = Brush(foreground),
                Margin = margin,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false
            };
        }

        var scheduleText = timeRange + customPeriod;
        var availableWidth = Math.Max(
            0,
            Timeline.ActualWidth - LabelWidth - 8 - margin.Left - margin.Right);
        if (string.IsNullOrEmpty(targetName))
        {
            return new TextBlock
            {
                Text = scheduleText,
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Foreground = Brush(foreground),
                Margin = margin,
                MaxWidth = availableWidth,
                VerticalAlignment = VerticalAlignment.Top,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false
            };
        }

        var twoLineTarget = new TextBlock
        {
            Text = targetName,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = Brush(foreground),
            MaxWidth = availableWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        var twoLineSchedule = new TextBlock
        {
            Text = scheduleText,
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = Brush(foreground),
            Margin = new Thickness(0, 1, 0, 0),
            MaxWidth = availableWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        twoLineTarget.Measure(new Size(availableWidth, double.PositiveInfinity));
        twoLineSchedule.Measure(new Size(availableWidth, double.PositiveInfinity));
        var availableHeight = Math.Max(0, end - start - margin.Top - margin.Bottom);
        var twoLineHeight = twoLineTarget.DesiredSize.Height
            + twoLineSchedule.Margin.Top
            + twoLineSchedule.DesiredSize.Height;
        if (twoLineHeight <= availableHeight)
        {
            var rows = new StackPanel
            {
                Margin = margin,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            rows.Children.Add(twoLineTarget);
            rows.Children.Add(twoLineSchedule);
            return rows;
        }

        var line = new Grid
        {
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var separator = new TextBlock
        {
            Text = "·",
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = Brush(foreground),
            Margin = new Thickness(4, 0, 4, 0)
        };
        var range = new TextBlock
        {
            Text = scheduleText,
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = Brush(foreground),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        separator.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        range.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        range.MaxWidth = Math.Max(0, availableWidth - separator.DesiredSize.Width);
        var target = new TextBlock
        {
            Text = targetName,
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = Brush(foreground),
            MaxWidth = Math.Max(
                0,
                availableWidth - separator.DesiredSize.Width - Math.Min(range.DesiredSize.Width, range.MaxWidth)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(range, 2);
        line.Children.Add(target);
        line.Children.Add(separator);
        line.Children.Add(range);
        return line;
    }
    private static Border MakeResizeHandle(RuleDragMode mode, VerticalAlignment alignment) => new()
    {
        Height = ResizeHandleHeight,
        VerticalAlignment = alignment,
        Background = Brushes.Transparent,
        Cursor = Cursors.SizeNS,
        Tag = mode
    };
    private void BeginRuleInteraction(
        AutomaticRuleItemViewModel rule,
        RuleDragMode mode,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (Model is not null) Model.SelectedRuleId = rule.Id;
        if (Model?.IsEditorOpen != false) return;
        _pressedRule = rule;
        _dragMode = mode;
        _pressPoint = e.GetPosition(TimelineScroll);
        _anchor = e.GetPosition(Timeline).Y - TopInset;
        _moving = false;
        Timeline.Cursor = mode is RuleDragMode.ResizeStart or RuleDragMode.ResizeEnd
            ? Cursors.SizeNS
            : Cursors.Hand;
        TimelineHint.Text = DefaultHint;
        Timeline.CaptureMouse();
    }
    private static string Time(double minute) => $"{(int)minute / 60:00}:{(int)minute % 60:00}";
    private static string Duration(double minutes) => minutes < 60
        ? $"{minutes:0}分钟"
        : $"{(int)minutes / 60}小时{((int)minutes % 60 == 0 ? "" : $"{(int)minutes % 60}分钟")}";
    private void Timeline_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Model is not null) Model.SelectedRuleId = null;
        if (Model is null || Model.IsEditorOpen || e.GetPosition(Timeline).X < LabelWidth) return;
        var minute = e.GetPosition(Timeline).Y - TopInset;
        if (RuleTimelineRange.Drag(minute, minute, Model.GetRules().Select(r => (r.StartMinutes, r.EndMinutes))) is null) return;
        TimelineHint.Text = DefaultHint;
        _anchor = minute;
        Timeline.CaptureMouse();
        _scrollTimer.Start();
        e.Handled = true;
    }
    private void Timeline_MouseMove(object sender, MouseEventArgs e) => UpdateDrag();
    private void UpdateDrag()
    {
        if (_anchor is null || Model is null) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }
        if (_pressedRule is not null)
        {
            if (!_moving)
            {
                if ((Mouse.GetPosition(TimelineScroll) - _pressPoint).Length < RuleTimelineRange.DragThreshold) return;
                _moving = true;
                _scrollTimer.Start();
            }
            var pointer = Mouse.GetPosition(Timeline).Y - TopInset;
            var occupied = Model.GetRules()
                .Where(rule => rule.Id != _pressedRule.Id)
                .Select(rule => (rule.StartMinutes, rule.EndMinutes));
            _draft = _dragMode switch
            {
                RuleDragMode.ResizeStart => RuleTimelineRange.Resize(
                    _pressedRule.StartMinutes, _pressedRule.EndMinutes, pointer, true, occupied),
                RuleDragMode.ResizeEnd => RuleTimelineRange.Resize(
                    _pressedRule.StartMinutes, _pressedRule.EndMinutes, pointer, false, occupied),
                _ => RuleTimelineRange.Move(
                    _pressedRule.StartMinutes, _pressedRule.EndMinutes, pointer - _anchor.Value)
            };
            RenderTimeline();
        }
        else
        {
            _draft = RuleTimelineRange.Drag(_anchor.Value, Mouse.GetPosition(Timeline).Y - TopInset,
                Model.GetRules().Select(r => (r.StartMinutes, r.EndMinutes)));
            DrawDraft();
        }
    }
    private void DrawDraft()
    {
        if (_draftBlock is not null) Timeline.Children.Remove(_draftBlock);
        _draftBlock = null;
        if (_draft is not { } range || range.End <= range.Start) return;
        _draftBlock = MakeBlock(
            range.Start,
            range.End,
            true,
            _pressedRule is null ? null : RuleTargetName(_pressedRule),
            _pressedRule is null ? null : RuleCustomPeriod(_pressedRule));
        _draftBlock.IsHitTestVisible = false;
        Timeline.Children.Add(_draftBlock);
    }
    private void AutoScroll()
    {
        if (_anchor is null) return;
        var y = Mouse.GetPosition(TimelineScroll).Y;
        var delta = y < 24 ? -12 : y > TimelineScroll.ActualHeight - 24 ? 12 : 0;
        if (delta != 0) { TimelineScroll.ScrollToVerticalOffset(TimelineScroll.VerticalOffset + delta); UpdateDrag(); }
    }
    private void Timeline_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_anchor is null) return;
        var range = _draft;
        var rule = _pressedRule;
        var moving = _moving;
        var dragMode = _dragMode;
        var releaseMinute = e.GetPosition(Timeline).Y - TopInset;
        _anchor = null; _pressedRule = null; _moving = false; _dragMode = RuleDragMode.Move;
        Timeline.ClearValue(CursorProperty);
        _scrollTimer.Stop(); Timeline.ReleaseMouseCapture();
        if (rule is not null)
        {
            _draft = null;
            if (!moving && dragMode == RuleDragMode.Move) Model?.EditRequested?.Invoke(rule);
            else if (range is { } moved)
            {
                if (dragMode == RuleDragMode.Move)
                {
                    var resolved = RuleTimelineRange.ResolveMove(
                        moved.Start,
                        moved.End,
                        releaseMinute,
                        Model?.GetRules().Where(candidate => candidate.Id != rule.Id)
                            .Select(candidate => (candidate.StartMinutes, candidate.EndMinutes)) ?? []);
                    TimelineHint.Text = resolved is { } placement
                        ? Model?.MoveRequested?.Invoke(rule, placement.Start, placement.End) ?? DefaultHint
                        : "没有可放置的空白时段，已恢复原位置";
                }
                else
                {
                    TimelineHint.Text = Model?.ResizeRequested?.Invoke(rule, moved.Start, moved.End) ?? DefaultHint;
                }
            }
            else if (moving) TimelineHint.Text = "当前位置无法调整，请重新拖动";
            RenderTimeline();
        }
        else if (range is { } r && r.End > r.Start) Model?.BeginEditor(null, r.Start, r.End);
        else CancelDrag();
        e.Handled = true;
    }
    private void Timeline_LostCapture(object sender, MouseEventArgs e) { if (_anchor is not null) CancelDrag(); }
    private void CancelDrag()
    {
        _anchor = null; _draft = null; _pressedRule = null; _moving = false; _dragMode = RuleDragMode.Move;
        Timeline.ClearValue(CursorProperty);
        _scrollTimer.Stop();
        if (Timeline.IsMouseCaptured) Timeline.ReleaseMouseCapture();
        RenderTimeline();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_anchor is not null) CancelDrag();
        else if (Model?.IsEditorOpen == true) Model.CancelEditor();
        else Model?.CloseCommand.Execute(null);
        e.Handled = true;
    }
}
