using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.ViewModels;

namespace FocusApp.Desktop.Views;

// The editor's content is hosted by AutomaticRuleModal in the owner's adorner layer.
// Neither the editor nor its dropdowns need a separate native window at runtime.
public partial class AutomaticRuleEditorWindow : Window
{
    public const double SurfaceInset = 10;
    private enum Picker { None, Start, End, Target }
    private readonly AutomaticRuleModalViewModel _model;
    private Picker _picker;
    private bool _updatingSelection;
    private bool _dismissPickerOnRelease;

    public AutomaticRuleEditorWindow(AutomaticRuleModalViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        EditorRoot.DataContext = model;
        EditorRoot.Resources = Resources;
        TextElement.SetFontFamily(EditorRoot, FontFamily);
        TextElement.SetFontSize(EditorRoot, FontSize);
        TextElement.SetFontWeight(EditorRoot, FontWeight);
        TextElement.SetForeground(EditorRoot, Foreground);
        _model.PropertyChanged += ModelChanged;
        UpdateTargetLabel();
        _model.StartImmediateEditing();
    }

    public FrameworkElement DetachSurface()
    {
        Content = null;
        return EditorRoot;
    }

    public void DisposeEditor()
    {
        _model.PropertyChanged -= ModelChanged;
        ClosePicker();
        if (EditorRoot.IsMouseCaptureWithin) Mouse.Capture(null);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _model.CancelEditor();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _model.DeleteEditor();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (_picker != Picker.None) ClosePicker();
        else _model.CancelEditor();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutomaticRuleModalViewModel.TargetChoice)
            or nameof(AutomaticRuleModalViewModel.SelectedTargetId)
            or nameof(AutomaticRuleModalViewModel.Targets)) UpdateTargetLabel();
        if (e.PropertyName is nameof(AutomaticRuleModalViewModel.EditorStartText)
            or nameof(AutomaticRuleModalViewModel.EditorEndText))
        {
            if (!_updatingSelection && _picker is Picker.Start or Picker.End) UpdateTimeSelection();
        }
        if (e.PropertyName == nameof(AutomaticRuleModalViewModel.IsCustom)) ClosePicker();
    }

    private void UpdateTargetLabel()
        => TargetField.Content = _model.Targets.FirstOrDefault(target => target.Id == _model.TargetChoice)?.Name ?? "未绑定";

    private void StartTime_Click(object sender, RoutedEventArgs e) { e.Handled = true; TogglePicker(Picker.Start); }
    private void EndTime_Click(object sender, RoutedEventArgs e) { e.Handled = true; TogglePicker(Picker.End); }
    private void Target_Click(object sender, RoutedEventArgs e) { e.Handled = true; TogglePicker(Picker.Target); }

    private void TogglePicker(Picker picker)
    {
        if (_picker == picker) { ClosePicker(); return; }
        ClosePicker();
        _picker = picker;
        var field = picker switch { Picker.Start => StartTimeField, Picker.End => EndTimeField, _ => TargetField };
        field.Tag = true;
        EditorRoot.UpdateLayout();
        var position = field.TranslatePoint(new Point(0, field.ActualHeight), EditorRoot);
        if (picker == Picker.Target)
        {
            _updatingSelection = true;
            TargetsList.SelectedItem = _model.Targets.FirstOrDefault(target => target.Id == _model.TargetChoice);
            _updatingSelection = false;
            TargetPickerSurface.Height = Math.Min(156, Math.Max(42, _model.Targets.Count * 28 + 12));
            // A long goal list opens above the field, without resizing the editor.
            if (position.Y + TargetPickerSurface.Height > 384)
                position.Y -= field.ActualHeight + TargetPickerSurface.Height + 3;
            Canvas.SetLeft(TargetPickerSurface, position.X);
            Canvas.SetTop(TargetPickerSurface, position.Y + 3);
            TargetPickerSurface.Visibility = Visibility.Visible;
            TargetsList.Focus();
        }
        else
        {
            TimePickerSurface.Height = Math.Min(178, Math.Max(100, 384 - position.Y - 3));
            Canvas.SetLeft(TimePickerSurface, Math.Clamp(position.X, 18, 262 - TimePickerSurface.Width));
            Canvas.SetTop(TimePickerSurface, position.Y + 3);
            TimePickerSurface.Visibility = Visibility.Visible;
            UpdateTimeSelection();
            EditorRoot.UpdateLayout();
            HoursList.Focus();
            var hourScroll = FindChild<ScrollViewer>(HoursList);
            if (hourScroll is not null)
            {
                var offset = Math.Max(0, HoursList.SelectedIndex - 2) * 28d;
                // Custom weekdays leave less dropdown height; keep the entire
                // selected row visible in that smaller viewport as well.
                offset = Math.Max(offset, (HoursList.SelectedIndex + 1) * 28 - hourScroll.ViewportHeight);
                hourScroll.ScrollToVerticalOffset(Math.Max(0, offset));
            }
            MinutesList.ScrollIntoView(MinutesList.SelectedItem);
        }
    }

    private void UpdateTimeSelection()
    {
        _updatingSelection = true;
        try
        {
            var value = (_picker == Picker.Start ? _model.EditorStartText : _model.EditorEndText).Split(':');
            var hour = int.TryParse(value.ElementAtOrDefault(0), out var h) ? h : 0;
            var minute = int.TryParse(value.ElementAtOrDefault(1), out var m) ? m : 0;
            var hourCount = _picker == Picker.End ? 25 : 24;
            if (HoursList.Items.Count != hourCount)
                HoursList.ItemsSource = Enumerable.Range(0, hourCount).Select(n => n.ToString("00")).ToArray();
            // Retain existing non-quarter-hour rules instead of silently rounding them.
            var minutes = (hour == 24 ? new[] { 0 } : new[] { 0, 15, 30, 45, minute }.Distinct().Order().ToArray())
                .Select(n => n.ToString("00")).ToArray();
            if (!MinutesList.Items.Cast<string>().SequenceEqual(minutes)) MinutesList.ItemsSource = minutes;
            HoursList.SelectedItem = hour.ToString("00");
            MinutesList.SelectedItem = minute.ToString("00");
        }
        finally { _updatingSelection = false; }
    }

    private void Time_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || _picker is not (Picker.Start or Picker.End)) return;
        if (HoursList.SelectedItem is not string hour || MinutesList.SelectedItem is not string minute) return;
        var text = $"{hour}:{(hour == "24" ? "00" : minute)}";
        if (_picker == Picker.Start) _model.EditorStartText = text;
        else _model.EditorEndText = text;
    }

    private void Target_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || _picker != Picker.Target || TargetsList.SelectedItem is not RuleTargetOption option) return;
        _model.TargetChoice = option.Id ?? string.Empty;
        // ListBox selection happens on mouse down; keep its surface alive until mouse up.
        if (Mouse.LeftButton == MouseButtonState.Pressed) _dismissPickerOnRelease = true;
        else ClosePicker();
    }

    private void Editor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_picker == Picker.None || e.ChangedButton != MouseButton.Left) return;
        var source = e.OriginalSource as DependencyObject;
        if (IsWithin(source, TimePickerSurface) || IsWithin(source, TargetPickerSurface)
            || IsWithin(source, StartTimeField) || IsWithin(source, EndTimeField) || IsWithin(source, TargetField)) return;
        e.Handled = true;
        _dismissPickerOnRelease = true;
        Mouse.Capture(EditorRoot, CaptureMode.SubTree);
    }

    private void Editor_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dismissPickerOnRelease || e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        _dismissPickerOnRelease = false;
        ClosePicker();
        if (EditorRoot.IsMouseCaptureWithin) Mouse.Capture(null);
    }

    private void ClosePicker()
    {
        _picker = Picker.None;
        _dismissPickerOnRelease = false;
        TimePickerSurface.Visibility = TargetPickerSurface.Visibility = Visibility.Collapsed;
        StartTimeField.Tag = EndTimeField.Tag = TargetField.Tag = false;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject root)
    {
        for (var current = source; current is not null;)
        {
            if (ReferenceEquals(current, root)) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
