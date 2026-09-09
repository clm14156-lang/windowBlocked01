using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public class RuleTimelineLayoutTests
{
    [Fact]
    public void CompletedBlocksShowOptionalEllipsizedTargetAndAlwaysKeepTimeRangeSeparate()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                const string longTarget = "这是一个很长很长需要被省略显示的学习目标名称";
                var settings = new SettingsPageViewModel([], []);
                settings.RuleModal.Targets.Add(new("goal-long", longTarget));
                settings.RuleModal.Targets.Add(new("goal-short", "学习 UE5"));
                settings.AutomaticRules.Add(new(Guid.NewGuid(), "每天", "01:00–02:00", ["Monday"], 60, 120));
                settings.AutomaticRules.Add(new(Guid.NewGuid(), "每天", "03:00–04:00", ["Monday"], 180, 240)
                {
                    TargetId = "goal-long"
                });
                settings.AutomaticRules.Add(new(Guid.NewGuid(), "每天", "05:00–05:15", ["Monday"], 300, 315)
                {
                    TargetId = "goal-short"
                });
                var view = new AutomaticRuleModal { DataContext = settings.RuleModal };
                view.Measure(new Size(370, 620));
                view.Arrange(new Rect(0, 0, 370, 620));
                view.UpdateLayout();

                var texts = Descendants(view).OfType<TextBlock>().ToArray();
                Assert.DoesNotContain(texts, text => text.Text.Contains("自动屏蔽", StringComparison.Ordinal));
                Assert.Contains(texts, text => text.Text == "01:00–02:00");
                Assert.Contains(texts, text => text.Text == "03:00–04:00");
                Assert.All(
                    texts.Where(text => text.Text is "01:00–02:00" or "03:00–04:00" or "05:00–05:15"),
                    text => Assert.Equal(12, text.FontSize));
                var target = Assert.Single(texts.Where(text => text.Text == longTarget));
                Assert.Equal(13, target.FontSize);
                Assert.Equal(TextTrimming.CharacterEllipsis, target.TextTrimming);
                Assert.Equal(TextWrapping.NoWrap, target.TextWrapping);
                Assert.True(double.IsFinite(target.MaxWidth));

                var shortTarget = Assert.Single(texts.Where(text => text.Text == "学习 UE5"));
                var line = Assert.IsType<Grid>(shortTarget.Parent);
                Assert.Equal(HorizontalAlignment.Left, line.HorizontalAlignment);
                Assert.All(line.ColumnDefinitions, column => Assert.True(column.Width.IsAuto));
                var separator = Assert.Single(line.Children.OfType<TextBlock>().Where(text => text.Text == "·"));
                Assert.Equal(new Thickness(4, 0, 4, 0), separator.Margin);
                Assert.Contains(line.Children.OfType<TextBlock>(), text => text.Text == "05:00–05:15");
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void CustomPeriodIsAlwaysShownAndTargetLayoutAdaptsToMeasuredHeight()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new SettingsPageViewModel([], []);
                settings.RuleModal.Targets.Add(new("goal-tall", "学习ue5"));
                settings.RuleModal.Targets.Add(new("goal-short", "短时目标"));
                settings.AutomaticRules.Add(new(Guid.NewGuid(), "周一 / 周二 / 周四", "04:45–06:15",
                    ["Thursday", "Monday", "Tuesday"], 285, 375, true));
                settings.AutomaticRules.Add(new(Guid.NewGuid(), "周一 / 周二 / 周三 / 周四", "07:00–08:00",
                    ["Wednesday", "Monday", "Thursday", "Tuesday"], 420, 480, true)
                {
                    TargetId = "goal-tall"
                });
                settings.AutomaticRules.Add(new(Guid.NewGuid(), "周一 / 周二 / 周三", "09:00–09:15",
                    ["Wednesday", "Monday", "Tuesday"], 540, 555, true)
                {
                    TargetId = "goal-short"
                });
                var view = new AutomaticRuleModal { DataContext = settings.RuleModal };
                view.Measure(new Size(370, 620));
                view.Arrange(new Rect(0, 0, 370, 620));
                view.UpdateLayout();

                var texts = Descendants(view).OfType<TextBlock>().ToArray();
                Assert.Contains(texts, text => text.Text == "04:45–06:15（周一、周二、周四）");

                var tallSchedule = Assert.Single(texts.Where(text =>
                    text.Text == "07:00–08:00（周一、周二、周三、周四）"));
                var rows = Assert.IsType<StackPanel>(tallSchedule.Parent);
                Assert.Equal(Orientation.Vertical, rows.Orientation);
                var tallTarget = Assert.Single(rows.Children.OfType<TextBlock>().Where(text => text.Text == "学习ue5"));
                Assert.Equal(13, tallTarget.FontSize);
                Assert.Equal(FontWeights.Medium, tallTarget.FontWeight);
                Assert.Equal(12, tallSchedule.FontSize);

                var shortSchedule = Assert.Single(texts.Where(text =>
                    text.Text == "09:00–09:15（周一、周二、周三）"));
                var line = Assert.IsType<Grid>(shortSchedule.Parent);
                Assert.Contains(line.Children.OfType<TextBlock>(), text => text.Text == "短时目标");
                Assert.Contains(line.Children.OfType<TextBlock>(), text => text.Text == "·");
                Assert.Equal(TextWrapping.NoWrap, shortSchedule.TextWrapping);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void CreatingBlockShowsOnlyLiveRangeAndDurationOnOneLine()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new SettingsPageViewModel([], []);
                var view = new AutomaticRuleModal { DataContext = settings.RuleModal };
                view.Measure(new Size(370, 620));
                view.Arrange(new Rect(0, 0, 370, 620));

                typeof(AutomaticRuleModal)
                    .GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(view, (ValueTuple<double, double>?)(120, 195));
                typeof(AutomaticRuleModal)
                    .GetMethod("RenderTimeline", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(view, null);
                view.UpdateLayout();

                var draftText = Assert.Single(Descendants(view).OfType<TextBlock>().Where(text =>
                    text.Text == "02:00–03:15 · 1小时15分钟"));
                Assert.Equal(TextWrapping.NoWrap, draftText.TextWrapping);
                Assert.DoesNotContain("自动屏蔽", draftText.Text, StringComparison.Ordinal);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void ExistingRuleHasSixDipTopAndBottomResizeHandles()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new SettingsPageViewModel([], []);
                settings.OpenRuleModalCommand.Execute(null);
                settings.RuleModal.BeginEditor(null, 60, 120);
                settings.RuleModal.SaveEditor();
                var view = new AutomaticRuleModal { DataContext = settings.RuleModal };
                view.Measure(new Size(370, 620));
                view.Arrange(new Rect(0, 0, 370, 620));
                view.UpdateLayout();

                var handles = Descendants(view)
                    .OfType<Border>()
                    .Where(border => border.Cursor == Cursors.SizeNS)
                    .ToArray();
                Assert.Equal(2, handles.Length);
                Assert.All(handles, handle => Assert.Equal(6, handle.Height));
                Assert.Contains(handles, handle => handle.VerticalAlignment == VerticalAlignment.Top);
                Assert.Contains(handles, handle => handle.VerticalAlignment == VerticalAlignment.Bottom);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void FloatingEditorSaveClosesEditorButKeepsTimelineOpen()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new SettingsPageViewModel([], []);
                settings.OpenRuleModalCommand.Execute(null);
                settings.RuleModal.BeginEditor(null, 60, 120);
                var editor = new AutomaticRuleEditorWindow(settings.RuleModal);
                var content = Assert.IsAssignableFrom<FrameworkElement>(editor.Content);
                content.Measure(new Size(300, double.PositiveInfinity));
                content.Arrange(new Rect(0, 0, 300, content.DesiredSize.Height));
                content.UpdateLayout();

                var save = Assert.Single(
                    Descendants(content).OfType<Button>().Where(button => Equals(button.Content, "保存")));
                Assert.Contains(
                    Descendants(content).OfType<Button>(),
                    button => Equals(button.Content, "删除"));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.True(settings.RuleModal.IsOpen);
                Assert.False(settings.RuleModal.IsEditorOpen);
                Assert.Single(settings.AutomaticRules);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void TimelineAndFloatingEditorFitAtMultipleRenderScales()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new SettingsPageViewModel([], []);
                settings.OpenRuleModalCommand.Execute(null);
                settings.RuleModal.BeginEditor(null, 180, 360);
                settings.RuleModal.SaveEditor();
                var view = new AutomaticRuleModal { DataContext = settings.RuleModal };
                view.Measure(new Size(370, 620));
                view.Arrange(new Rect(0, 0, 370, 620));
                view.UpdateLayout();
                Assert.Equal(370, view.ActualWidth);
                var scroll = (ScrollViewer)view.FindName("TimelineScroll");
                Assert.True(scroll.ScrollableHeight > 800);
                var thumb = Descendants(scroll).OfType<System.Windows.Controls.Primitives.Thumb>().First();
                var thumbBody = Assert.Single(Descendants(thumb).OfType<Border>());
                Assert.Equal(4, thumbBody.ActualWidth);
                Assert.True(thumbBody.ActualHeight > 10, $"Thumb height: {thumbBody.ActualHeight}");
                var thumbLocation = thumbBody.TransformToAncestor(view).Transform(new Point());
                Assert.InRange(thumbLocation.X, 0, 370 - thumbBody.ActualWidth);
                Assert.InRange(thumbLocation.Y, 0, 620 - thumbBody.ActualHeight);
                for (DependencyObject? ancestor = thumbBody; ancestor is FrameworkElement element; ancestor = VisualTreeHelper.GetParent(ancestor))
                {
                    Assert.Equal(Visibility.Visible, element.Visibility);
                    Assert.True(element.Opacity > 0);
                }
                scroll.ScrollToVerticalOffset(600);
                view.UpdateLayout();
                Assert.Equal(600, scroll.VerticalOffset);
                scroll.ScrollToTop();
                view.UpdateLayout();
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                {
                    Render(view, scale, "timeline");
                    settings.RuleModal.BeginEditor(null, 420, 480);
                    settings.RuleModal.IsCustom = true;
                    var editor = new AutomaticRuleEditorWindow(settings.RuleModal);
                    var content = Assert.IsAssignableFrom<FrameworkElement>(editor.Content);
                    content.Measure(new Size(300, double.PositiveInfinity));
                    content.Arrange(new Rect(0, 0, 300, content.DesiredSize.Height));
                    content.UpdateLayout();
                    var buttons = Descendants(content).OfType<Button>().Where(b => Equals(b.Content, "保存")).ToArray();
                    var save = Assert.Single(buttons);
                    var location = save.TransformToAncestor(content).Transform(new Point());
                    Assert.InRange(location.Y + save.ActualHeight, 1, content.ActualHeight);
                    foreach (var weekday in Descendants(content).OfType<ToggleButton>()
                                 .Where(button => button.Content is string text && "一二三四五六日".Contains(text)))
                    {
                        var weekdayLocation = weekday.TransformToAncestor(content).Transform(new Point());
                        Assert.InRange(weekdayLocation.X, 0, content.ActualWidth - weekday.ActualWidth);
                    }
                    Render(content, scale, "floating-editor", 300, content.DesiredSize.Height);
                    settings.RuleModal.CancelEditor();
                }
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Render(Visual view, double scale, string name)
        => Render(view, scale, name, 370, 620);
    private static void Render(Visual view, double scale, string name, double width, double height)
    {
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(AppContext.BaseDirectory, $"{name}-{scale:0.##}.png");
        using var output = File.Create(path); encoder.Save(output);
    }
}
