using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class AutomaticRuleListPresentationTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("goal", null, false)]
    [InlineData("goal", "", false)]
    [InlineData("goal", " \t ", false)]
    [InlineData(null, "目标名称", false)]
    [InlineData(" \t", "目标名称", false)]
    [InlineData("goal", "开发屏蔽软件", true)]
    public void EmptyOrUnboundTargetsUseTheCenteredSingleLine(string? id, string? name, bool hasTarget)
    {
        Sta(() =>
        {
            using var ui = new ListView();
            var rule = Rule(true); rule.SetTarget(id, name); ui.Add(rule);
            var row = ui.Row(rule); var target = Find<TextBlock>(row, "RuleTargetText");
            var time = Find<TextBlock>(row, "RuleTimeText");
            Assert.Equal(hasTarget, rule.HasDisplayTarget);
            Assert.Equal(id, rule.TargetId); // Presentation never clears the binding data.
            Assert.Equal(hasTarget ? Visibility.Visible : Visibility.Collapsed, target.Visibility);
            Assert.Equal(hasTarget ? 12 : 13, time.FontSize);
            Assert.Equal(hasTarget ? 400 : 500, time.FontWeight.ToOpenTypeWeight());
            Assert.Equal(13, target.FontSize); Assert.Equal(500, target.FontWeight.ToOpenTypeWeight());
            Assert.Equal(44, row.ActualHeight);
            if (!hasTarget)
            {
                Assert.Empty(target.Text);
                Assert.Equal(new Thickness(), Find<Grid>(row, "RuleScheduleLine").Margin);
                Assert.InRange(Math.Abs(Center(time, row).Y - 22), 0, 1);
            }
            else Assert.True(time.TranslatePoint(new Point(), row).Y > target.TranslatePoint(new Point(), row).Y);
            Assert.Equal(12, Find<TextBlock>(row, "RuleRepeatText").FontSize);
            Assert.Equal(400, Find<TextBlock>(row, "RuleRepeatText").FontWeight.ToOpenTypeWeight());
        });
    }

    [Fact]
    public void EnabledDisabledTargetAndRepeatChangesKeepControlsAlignedAndInteractive()
    {
        Sta(() =>
        {
            using var ui = new ListView();
            var targeted = Rule(false); targeted.SetTarget("goal", "开发屏蔽软件");
            var repeating = Rule(true); var plain = Rule(false); var disabled = Rule(false); disabled.IsEnabled = false;
            foreach (var item in new[] { targeted, repeating, plain, disabled }) ui.Add(item);
            var toggleXs = new List<double>(); var menuXs = new List<double>();
            foreach (var item in ui.Model.AutomaticRules)
            {
                var row = ui.Row(item);
                var toggle = Switch(row); var menu = Find<ToggleButton>(row, "RuleMoreButton");
                toggleXs.Add(toggle.TranslatePoint(new Point(), row).X); menuXs.Add(menu.TranslatePoint(new Point(), row).X);
                Assert.Equal(44, row.ActualHeight);
                Assert.InRange(Math.Abs(Center(toggle, row).Y - 22), 0, 1);
                Assert.InRange(Math.Abs(Center(menu, row).Y - 22), 0, 1);
                Assert.InRange(Math.Abs(Center(Find<System.Windows.Shapes.Ellipse>(row, "RuleStatusDot"), row).Y - 22), 0, 1);
                Assert.Equal(item.HasScheduleRepeat ? Visibility.Visible : Visibility.Collapsed, Find<TextBlock>(row, "RuleRepeatText").Visibility);
                Assert.True(toggle.IsEnabled); Assert.True(menu.IsEnabled);
            }
            Assert.All(toggleXs, value => Assert.Equal(toggleXs[0], value));
            Assert.All(menuXs, value => Assert.Equal(menuXs[0], value));
            var disabledRow = ui.Row(disabled); var before = Center(Find<TextBlock>(disabledRow, "RuleTimeText"), disabledRow);
            Assert.Equal(ColorOf(ui.Page.FindResource("TextWeak")), ColorOf(Find<TextBlock>(disabledRow, "RuleTimeText").Foreground));
            Assert.Equal(ColorOf(ui.Page.FindResource("TextWeak")), ColorOf(Find<System.Windows.Shapes.Ellipse>(disabledRow, "RuleStatusDot").Fill));
            Click(Switch(disabledRow)); ui.Layout();
            Assert.True(disabled.IsEnabled);
            Assert.Equal(before, Center(Find<TextBlock>(disabledRow, "RuleTimeText"), disabledRow));
            Assert.Equal(ColorOf(ui.Page.FindResource("AccentPrimary")), ColorOf(Find<System.Windows.Shapes.Ellipse>(disabledRow, "RuleStatusDot").Fill));
            Assert.Equal(ColorOf(ui.Page.FindResource("TextPrimary")), ColorOf(Find<TextBlock>(disabledRow, "RuleTimeText").Foreground));
            disabled.SetTarget("goal", "新增目标"); ui.Layout();
            Assert.Equal(12, Find<TextBlock>(disabledRow, "RuleTimeText").FontSize);
            disabled.SetTarget("goal", " "); ui.Layout();
            Assert.Equal(13, Find<TextBlock>(disabledRow, "RuleTimeText").FontSize);
            Assert.Equal(before, Center(Find<TextBlock>(disabledRow, "RuleTimeText"), disabledRow));
            repeating.Update("", repeating.TimeRangeText, [], 45, 75, true); ui.Layout();
            Assert.False(repeating.HasScheduleRepeat); Assert.Empty(repeating.ScheduleRepeatDisplayText);
            Assert.Equal(Visibility.Collapsed, Find<TextBlock>(ui.Row(repeating), "RuleRepeatText").Visibility);
            Assert.All(ui.Model.AutomaticRules, item => Assert.Equal(toggleXs[0], Switch(ui.Row(item)).TranslatePoint(new Point(), ui.Row(item)).X));
            repeating.Update("周一 / 周三 / 周五", repeating.TimeRangeText, [], 45, 75, true);
            disabled.IsEnabled = false; ui.Layout(); ui.Render("automatic-rule-states");
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LongRepeatTextYieldsWidthToTheCompleteTimeAndRightControls(bool hasTarget)
    {
        Sta(() =>
        {
            using var ui = new ListView();
            var rule = Rule(true); if (hasTarget) rule.SetTarget("goal", "有目标且周期很长");
            rule.Update(string.Join(" / ", Enumerable.Repeat("周一 / 周二 / 周三 / 周四 / 周五 / 周六 / 周日", 3)), "00:45 – 01:15", [], 45, 75, true);
            ui.Add(rule); ui.List.Width = 330; ui.Layout();
            var row = ui.Row(rule); var time = Find<TextBlock>(row, "RuleTimeText"); var repeat = Find<TextBlock>(row, "RuleRepeatText");
            var fullTime = MeasureText(time); var fullRepeat = MeasureText(repeat);
            Assert.InRange(Math.Abs(time.ActualWidth - fullTime), 0, 2);
            Assert.True(fullRepeat > repeat.ActualWidth + 100);
            Assert.Equal(TextTrimming.CharacterEllipsis, repeat.TextTrimming);
            Assert.True(repeat.TranslatePoint(new Point(), row).X + repeat.ActualWidth <= Switch(row).TranslatePoint(new Point(), row).X);
            Assert.Equal("00:45 – 01:15", time.Text);
            ui.Render(hasTarget ? "long-repeat-target" : "long-repeat-no-target");
        });
    }

    private sealed class ListView : IDisposable
    {
        public SettingsPageViewModel Model { get; } = new([new("AutomaticBlocking", "自动屏蔽", "", "", true)], []);
        public UserControl Page { get; }
        public ItemsControl List { get; }
        public ListView()
        {
            // Load the production visual tree with global resources in scope before its styles.
            // Use an isolated UserControl rather than creating a process-wide WPF Application.
            var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "FocusApp.sln"))) directory = directory.Parent;
            var source = XElement.Load(System.IO.Path.Combine(directory!.FullName, "src/FocusApp.Desktop/Views/SettingsPage.xaml"));
            XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            source.Attribute(x + "Class")!.Remove();
            // Event handlers are code-behind; the existing interaction tests verify their wiring.
            foreach (var attribute in source.DescendantsAndSelf().Attributes().Where(attribute =>
                attribute.Name.LocalName is "MouseEnter" or "MouseLeave" or "Click" or "PreviewMouseLeftButtonDown").ToArray()) attribute.Remove();
            var resources = source.Element(p + "UserControl.Resources")!;
            var localResources = resources.Elements().ToArray();
            var dictionary = new XElement(p + "ResourceDictionary", new XElement(p + "ResourceDictionary.MergedDictionaries",
                new[] { "Colors", "Spacing", "Typography", "Strings", "Styles" }.Select(resource =>
                    new XElement(p + "ResourceDictionary", new XAttribute("Source", $"/FocusApp.Desktop;component/Resources/{resource}.xaml")))));
            dictionary.Add(localResources); resources.ReplaceNodes(dictionary);
            Page = (UserControl)System.Windows.Markup.XamlReader.Parse(source.ToString());
            Page.DataContext = Model;
            List = (ItemsControl)Page.FindName("AutomaticRulesList");
        }
        public void Add(AutomaticRuleItemViewModel item) { Model.AutomaticRules.Add(item); Layout(); }
        public void Layout()
        {
            Page.Measure(new Size(730, 660)); Page.Arrange(new Rect(0, 0, 730, 660)); Page.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Page.UpdateLayout();
        }
        public Border Row(AutomaticRuleItemViewModel rule) => Descendants<Border>(List).Single(item => item.Name == "AutomaticRuleRow" && ReferenceEquals(item.DataContext, rule));
        public void Render(string name)
        {
            var path = Environment.GetEnvironmentVariable("FOCUSAPP_RULE_LIST_QA_PATH"); if (string.IsNullOrEmpty(path)) return;
            System.IO.Directory.CreateDirectory(path);
            var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, List.ActualWidth, List.ActualHeight));
                context.DrawRectangle(new VisualBrush(List), null, new Rect(0, 0, List.ActualWidth, List.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(List.ActualWidth), (int)Math.Ceiling(List.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(System.IO.Path.Combine(path, name + ".png")); png.Save(output);
        }
        public void Dispose() { Page.DataContext = null; }
    }
    private static AutomaticRuleItemViewModel Rule(bool repeat) => new(Guid.NewGuid(), repeat ? "周一 / 周三 / 周五" : "每天", "00:45 – 01:15", isCustom: repeat);
    private static ToggleButton Switch(DependencyObject row) => Descendants<ToggleButton>(row).Single(item => item.Name != "RuleMoreButton");
    private static Point Center(FrameworkElement item, Visual root) => item.TranslatePoint(new Point(item.ActualWidth / 2, item.ActualHeight / 2), (UIElement)root);
    private static Color ColorOf(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static double MeasureText(TextBlock text) => new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;
    private static void Click(ButtonBase button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement => Descendants<T>(root).Single(item => item.Name == name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Sta(Action action)
    {
        Exception? failure = null; var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new InvalidOperationException("Automatic rule presentation failed.", failure);
    }
}
