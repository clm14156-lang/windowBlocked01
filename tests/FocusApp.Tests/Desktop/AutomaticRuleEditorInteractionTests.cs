using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusApp.Contracts;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class AutomaticRuleEditorInteractionTests
{
    [Fact]
    public void TimeColumnsUpdateTheRuleImmediatelyAndOnlyOneDropdownIsOpen() => OnSta(() =>
    {
        var settings = CreateSettings();
        var rule = settings.AutomaticRules[0];
        settings.EditRuleCommand.Execute(rule);
        var editor = new AutomaticRuleEditorWindow(settings.RuleModal);
        var surface = Layout(editor);
        var start = (Button)editor.FindName("StartTimeField");
        var end = (Button)editor.FindName("EndTimeField");
        var hours = (ListBox)editor.FindName("HoursList");
        var minutes = (ListBox)editor.FindName("MinutesList");
        var picker = (Border)editor.FindName("TimePickerSurface");
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        surface.UpdateLayout();
        Assert.Equal("02", hours.SelectedItem);
        Assert.Equal("30", minutes.SelectedItem);
        Assert.Equal(new[] { "00", "15", "30", "45" }, minutes.Items.Cast<string>());
        var originalHourRow = hours.ItemContainerGenerator.ContainerFromItem("01");
        hours.SelectedItem = "01";
        Assert.Equal(90, rule.StartMinutes);
        Assert.Same(originalHourRow, hours.ItemContainerGenerator.ContainerFromItem("01"));
        minutes.SelectedItem = "15";
        Assert.Equal(75, rule.StartMinutes);
        Assert.True(settings.RuleModal.IsEditorOpen);

        end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False((bool)start.Tag);
        Assert.True((bool)end.Tag);
        Assert.Equal("03", hours.SelectedItem);
        Assert.Equal("45", minutes.SelectedItem);
        minutes.SelectedItem = "30";
        Assert.Equal(210, rule.EndMinutes);
        settings.RuleModal.IsCustom = true;
        Assert.Equal(Visibility.Collapsed, picker.Visibility);
        surface.UpdateLayout();
        end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        surface.UpdateLayout();
        var bounds = picker.TransformToAncestor(surface).TransformBounds(new Rect(picker.RenderSize));
        Assert.InRange(bounds.Bottom, 1, 400);
        var selectedHour = Assert.IsType<ListBoxItem>(hours.ItemContainerGenerator.ContainerFromItem(hours.SelectedItem));
        var hourBounds = selectedHour.TransformToAncestor(hours).TransformBounds(new Rect(selectedHour.RenderSize));
        Assert.InRange(hourBounds.Top, 0, hours.ActualHeight);
        Assert.InRange(hourBounds.Bottom, 1, hours.ActualHeight);
        Assert.Equal(new Size(280, 400), surface.RenderSize);
        end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Collapsed, picker.Visibility);
        Assert.Equal(rule.Id, Assert.Single(settings.AutomaticRules).Id);
        settings.RuleModal.CancelEditor();
        Assert.Equal(75, rule.StartMinutes);
        Assert.Equal(210, rule.EndMinutes);
        editor.DisposeEditor();
    });

    [Fact]
    public void TargetBindingAutoSavesAndDeleteRemovesOnlyTheEditedRule() => OnSta(() =>
    {
        var settings = CreateSettings();
        var now = DateTimeOffset.UtcNow;
        settings.RuleModal.ApplyTargets([new LocalTargetDto("goal", "学习", false, 0, now, now)]);
        var rule = settings.AutomaticRules[0];
        settings.EditRuleCommand.Execute(rule);
        var editor = new AutomaticRuleEditorWindow(settings.RuleModal);
        Layout(editor);
        ((Button)editor.FindName("TargetField")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((ListBox)editor.FindName("TargetsList")).SelectedItem = settings.RuleModal.Targets[1];
        Assert.Equal("goal", rule.TargetId);
        Assert.Equal("学习", ((Button)editor.FindName("TargetField")).Content);
        Assert.True(settings.RuleModal.IsEditorOpen);
        ((Button)editor.FindName("DeleteRuleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(settings.AutomaticRules);
        Assert.False(settings.RuleModal.IsEditorOpen);
        Assert.True(settings.RuleModal.IsOpen);
        editor.DisposeEditor();
    });

    [Fact]
    public void ClickingOutsidePickerConsumesDownAndUpBeforeHidingIt() => OnSta(() =>
    {
        var settings = CreateSettings();
        settings.EditRuleCommand.Execute(settings.AutomaticRules[0]);
        var editor = new AutomaticRuleEditorWindow(settings.RuleModal);
        var surface = Layout(editor);
        ((Button)editor.FindName("StartTimeField")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var picker = (Border)editor.FindName("TimePickerSurface");
        var delete = (Button)editor.FindName("DeleteRuleButton");
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent };
        delete.RaiseEvent(down);
        Assert.True(down.Handled);
        Assert.Equal(Visibility.Visible, picker.Visibility);
        var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 2, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseUpEvent };
        surface.RaiseEvent(up);
        Assert.True(up.Handled);
        Assert.Equal(Visibility.Collapsed, picker.Visibility);
        Assert.Single(settings.AutomaticRules);
        Assert.True(settings.RuleModal.IsEditorOpen);
        editor.DisposeEditor();
    });

    [Fact]
    public void RapidRepeatedTimelineClicksPreserveTheGestureAndShareTheOwnerWindow() => OnSta(() =>
    {
        var settings = CreateSettings();
        settings.OpenRuleModalCommand.Execute(null);
        var view = new AutomaticRuleModal { DataContext = settings.RuleModal };
        var root = new Grid { Background = Brushes.White };
        root.Children.Add(view);
        var owner = new Window
        {
            Width = 920, Height = 700, Left = -10000, Top = -10000,
            ShowActivated = false, ShowInTaskbar = false, Content = root
        };
        try
        {
            owner.Show();
            owner.UpdateLayout();
            var ownerSource = Assert.IsType<HwndSource>(PresentationSource.FromVisual(root));
            var nativeSources = PresentationSource.CurrentSources.Cast<PresentationSource>().ToArray();
            var timeline = (Canvas)view.FindName("Timeline");
            for (var index = 0; index < 30; index++)
            {
                var block = Assert.Single(timeline.Children.OfType<Border>().Where(border => border.Tag is Guid));
                var editorWasOpen = settings.RuleModal.IsEditorOpen;
                var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, index * 2, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
                block.RaiseEvent(down);
                Assert.True(down.Handled);
                Assert.Contains(block, timeline.Children.Cast<UIElement>());
                Assert.Equal(editorWasOpen, settings.RuleModal.IsEditorOpen);
                // Even a refreshed service snapshot must not replace the pressed block.
                settings.RuleModal.RefreshTimeline();
                Assert.Contains(block, timeline.Children.Cast<UIElement>());
                var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, index * 2 + 1, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent };
                timeline.RaiseEvent(up);
                owner.UpdateLayout();
                Assert.True(up.Handled);
                Assert.NotSame(timeline, Mouse.Captured);
                Assert.Equal(!editorWasOpen, settings.RuleModal.IsEditorOpen);
                Assert.Equal(nativeSources, PresentationSource.CurrentSources.Cast<PresentationSource>());
                if (settings.RuleModal.IsEditorOpen)
                {
                    var adorner = Assert.Single(AdornerLayer.GetAdornerLayer(root).GetAdorners(root));
                    var editorRoot = Assert.Single(Descendants(adorner).OfType<Grid>().Where(grid => grid.Name == "EditorRoot"));
                    Assert.Same(ownerSource, PresentationSource.FromVisual(editorRoot));
                    var time = Assert.Single(Descendants(editorRoot).OfType<Button>().Where(button => button.Name == "StartTimeField"));
                    time.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    owner.UpdateLayout();
                    var dropdown = Assert.Single(Descendants(editorRoot).OfType<Border>().Where(border => border.Name == "TimePickerSurface"));
                    Assert.Same(ownerSource, PresentationSource.FromVisual(dropdown));
                    Assert.Equal(nativeSources, PresentationSource.CurrentSources.Cast<PresentationSource>());
                }
            }
        }
        finally { settings.RuleModal.CancelEditor(); owner.Close(); }
    });

    private static SettingsPageViewModel CreateSettings()
    {
        var settings = new SettingsPageViewModel([], [], dailyLabel: "每天");
        settings.ApplyAutomaticRules([new LocalAutomaticRuleDto(Guid.NewGuid(), Enum.GetValues<DayOfWeek>(), 150, 225, true, 0)]);
        return settings;
    }
    private static FrameworkElement Layout(AutomaticRuleEditorWindow editor)
    {
        var content = (FrameworkElement)editor.Content;
        content.Measure(new Size(280, 400)); content.Arrange(new Rect(0, 0, 280, 400)); content.UpdateLayout();
        return content;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(12)));
        Assert.Null(failure);
    }
}
