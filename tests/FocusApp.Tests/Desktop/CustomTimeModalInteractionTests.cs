using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class CustomTimeModalInteractionTests
{
    [Fact]
    public void ClicksRenderUpToFourActiveButtonsAndAllPointerStatesKeepTheCardFixed()
    {
        RunSta(() =>
        {
            var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", false, 60),
                new("90", "", false, 90), new("180", "", false, 180), new("45", "", false, 45)]);
            var model = home.CustomTimeModal;
            model.Open();
            model.SelectTimeCommand.Execute(model.CommonTimes[0]);
            var modal = new CustomTimeModal { DataContext = model };
            AddResources(modal);
            Layout(modal, 320, 420);
            var card = (Border)modal.FindName("CustomTimeCard");
            var items = (ItemsControl)modal.FindName("CommonTimeOptions");
            var renderedButtons = Descendants<Button>(items).Where(button => Equals(button.Command, model.SelectTimeCommand)).ToArray();
            Assert.Equal(new[] { 30, 45, 60, 90, 180 }, renderedButtons.Select(button => ((HomeDurationOptionViewModel)button.CommandParameter).Minutes));
            // Exercise the existing pointer-state sequence independently of the numerical layout.
            var buttons = new[] { 30, 60, 90, 180, 45 }.Select(minutes => renderedButtons.Single(button =>
                ((HomeDurationOptionViewModel)button.CommandParameter).Minutes == minutes)).ToArray();
            Assert.Equal(5, buttons.Length);
            Assert.All(buttons, button =>
            {
                Assert.Equal(Color.FromRgb(229, 229, 234), BrushColor(button.BorderBrush));
                Assert.Equal(Colors.White, BrushColor(button.Background));
                Assert.Same(Cursors.Hand, button.Cursor);
                Assert.NotNull(button.FocusVisualStyle);
            });
            Render(modal, "custom-time-default");
            var size = new Size(card.ActualWidth, card.ActualHeight);
            var first = buttons[0];
            SetHover(first, true);
            Layout(modal, 320, 420);
            Assert.Equal(Color.FromRgb(255, 247, 240), BrushColor(first.Background));
            Assert.Equal(Color.FromRgb(255, 211, 176), BrushColor(first.BorderBrush));
            Assert.Equal(Color.FromRgb(58, 58, 60), BrushColor(first.Foreground));
            Render(modal, "custom-time-hover");
            SetPressed(first, true);
            Layout(modal, 320, 420);
            Assert.Equal(Color.FromRgb(255, 235, 221), BrushColor(first.Background));
            Render(modal, "custom-time-pressed");
            SetPressed(first, false);
            Assert.Equal(Color.FromRgb(255, 247, 240), BrushColor(first.Background));
            SetHover(first, false);
            Assert.Equal(Colors.White, BrushColor(first.Background));

            Invoke(first); Layout(modal, 320, 420);
            Assert.Equal("0", ((TextBox)modal.FindName("MinutesTextBox")).Text);
            AssertActive(first);
            Invoke(buttons[1]); Layout(modal, 320, 420);
            AssertActive(buttons[1]);
            AssertActive(first);
            Assert.Equal(2, buttons.Count(button => button.Tag is true));
            var input = (TextBox)modal.FindName("MinutesTextBox");
            input.Text = "90"; Layout(modal, 320, 420);
            Assert.False(buttons[2].Tag is true);
            Assert.Equal(2, buttons.Count(button => button.Tag is true));
            Invoke(buttons[2]); Invoke(buttons[3]); Layout(modal, 320, 420);
            Assert.Equal(4, buttons.Count(button => button.Tag is true));
            Assert.All(buttons.Take(4), AssertActive);
            Render(modal, "custom-time-active");
            input.Text = "45"; Layout(modal, 320, 420);
            Assert.Equal(4, buttons.Count(button => button.Tag is true));
            Invoke(buttons[4]); Layout(modal, 320, 420);
            Assert.Equal([60, 90, 180, 45], model.SelectedMinutes);
            Assert.Equal(Color.FromRgb(229, 229, 234), BrushColor(first.BorderBrush));
            Assert.All(buttons.Skip(1), AssertActive);
            Invoke(buttons[1]); Layout(modal, 320, 420);
            Assert.Equal(3, buttons.Count(button => button.Tag is true));
            Assert.Equal(Color.FromRgb(229, 229, 234), BrushColor(buttons[1].BorderBrush));
            Invoke(buttons[1]); Layout(modal, 320, 420);
            Assert.Equal([90, 180, 45, 60], model.SelectedMinutes);
            Assert.Equal("45", input.Text);
            Assert.Equal(model.SelectedMinutes.OrderBy(minutes => minutes), home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
            Render(modal, "custom-time-multiple-selected");
            Assert.Equal(size, new Size(card.ActualWidth, card.ActualHeight));
            Assert.All(buttons, button =>
            {
                Assert.Equal(86, button.Width);
                Assert.Equal(40, button.Height);
            });

            foreach (var button in Descendants<Button>(modal).Where(button =>
                Equals(button.Command, model.ToggleEditCommand) || Equals(button.Command, model.CancelCommand) || Equals(button.Command, model.ConfirmCommand)))
            {
                var border = Descendants<Border>(button).First();
                var normal = BrushColor(border.Background);
                SetHover(button, true);
                var hover = BrushColor(border.Background);
                SetPressed(button, true);
                var pressed = BrushColor(border.Background);
                Assert.NotEqual(normal, hover);
                Assert.NotEqual(hover, pressed);
                SetPressed(button, false); SetHover(button, false);
                Assert.Equal(normal, BrushColor(border.Background));
                Assert.Same(Cursors.Hand, button.Cursor);
            }
            Assert.Contains(Descendants<TextBlock>(modal), text => text.Text == "添加");
        });
    }

    [Fact]
    public void CommonTimesWrapInNumericOrderAndClickingDoesNotMoveTheirButtons()
    {
        RunSta(() =>
        {
            var home = new HomePageViewModel(new[] { 455, 180, 50, 54, 60, 90, 30, 16, 5 }
                .Select(minutes => new HomeDurationOptionViewModel(minutes.ToString(), "", minutes == 30, minutes)));
            var model = home.CustomTimeModal;
            model.Open();
            var modal = new CustomTimeModal { DataContext = model };
            AddResources(modal); Layout(modal, 320, 420);
            var items = (ItemsControl)modal.FindName("CommonTimeOptions");
            var expected = new[] { 5, 16, 30, 50, 54, 60, 90, 180, 455 };
            var positions = Positions();
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.InRange(positions[expected[index]].X, index % 3 * 93 - 1, index % 3 * 93 + 1);
                Assert.InRange(positions[expected[index]].Y, index / 3 * 48 - 1, index / 3 * 48 + 1);
            }
            foreach (var minutes in new[] { 30, 180, 60, 16, 5, 54, 16, 16 })
            {
                var button = Buttons().Single(button => ((HomeDurationOptionViewModel)button.CommandParameter).Minutes == minutes);
                Invoke(button); Layout(modal, 320, 420);
                Assert.Equal(positions, Positions());
            }
            Assert.Equal(new[] { 60, 5, 54, 16 }, model.SelectedMinutes);
            Assert.Equal(new[] { 5, 16, 54, 60 }, home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
            Render(modal, "common-time-numeric-order");
            home.ApplyDurationPresets(home.GetDurationPresets());
            Layout(modal, 320, 420);
            Assert.Equal(positions, Positions());
            model.CancelCommand.Execute(null); model.Open(); Layout(modal, 320, 420);
            Assert.Equal(positions, Positions());
            Assert.Equal(new[] { 60, 5, 54, 16 }, model.SelectedMinutes);
            Button[] Buttons() => Descendants<Button>(items).Where(button => Equals(button.Command, model.SelectTimeCommand)).ToArray();
            Dictionary<int, Point> Positions()
            {
                Assert.Equal(expected, Buttons().Select(button => ((HomeDurationOptionViewModel)button.CommandParameter).Minutes));
                return Buttons().ToDictionary(button => ((HomeDurationOptionViewModel)button.CommandParameter).Minutes,
                    button => button.TranslatePoint(new Point(), items));
            }
        });
    }

    [Fact]
    public void AddRemainsVisibleAtFullWidthWithGrayDisabledStateAndAnIndependentCloseEntry()
    {
        RunSta(() =>
        {
            var home = new HomePageViewModel([new("30", "", true, 30)]);
            var model = home.CustomTimeModal;
            model.Open();
            var modal = new CustomTimeModal { DataContext = model };
            AddResources(modal); Layout(modal, 320, 420);
            var add = (Button)modal.FindName("AddCustomTimeButton");
            var close = (Button)modal.FindName("CloseCustomTimeButton");
            Assert.Equal(280, add.ActualWidth);
            Assert.Equal(Visibility.Visible, add.Visibility);
            Assert.False(add.IsEnabled);
            Assert.Equal(Color.FromRgb(234, 234, 237), BrushColor(Descendants<Border>(add).First().Background));
            Assert.Single(Descendants<Button>(modal).Where(button => Equals(button.Command, model.CancelCommand)));
            Assert.DoesNotContain(Descendants<TextBlock>(modal), text => text.Text == "取消");
            Render(modal, "custom-time-disabled");
            model.MinutesInput = "45"; Layout(modal, 320, 420);
            Assert.True(add.IsEnabled);
            Assert.Equal(Color.FromRgb(255, 122, 0), BrushColor(Descendants<Border>(add).First().Background));
            Render(modal, "custom-time-add-ready");
            Invoke(add); Layout(modal, 320, 420);
            Assert.Equal("0", ((TextBox)modal.FindName("MinutesTextBox")).Text);
            Assert.False(add.IsEnabled);
            Assert.Equal([30], model.SelectedMinutes);
            Assert.Single(model.CommonTimes.Where(option => option.Minutes == 45));
            Assert.False(model.CommonTimes.Single(option => option.Minutes == 45).IsSelectedInCustomTime);
            Assert.True(model.IsOpen);
            Render(modal, "custom-time-added");
            Invoke(close); Layout(modal, 320, 420);
            Assert.False(model.IsOpen);
            Assert.Equal([30], home.VisibleDurationOptions.Where(option => option.Icon.Length == 0).Select(option => option.Minutes));
        });
    }

    private static void AssertActive(Button button)
    {
        Assert.True(button.Tag is true);
        Assert.Equal(Color.FromRgb(255, 122, 0), BrushColor(button.BorderBrush));
        Assert.Equal(Color.FromRgb(255, 122, 0), BrushColor(button.Foreground));
        var label = Assert.Single(Descendants<TextBlock>(button));
        Assert.Equal(BrushColor(button.Foreground), BrushColor(label.Foreground));
    }
    private static Color BrushColor(Brush brush) => ((SolidColorBrush)brush).Color;
    // Drive WPF's read-only input properties without moving the user's cursor.
    private static void SetHover(Button button, bool value) => button.SetValue(
        (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, value);
    private static void SetPressed(Button button, bool value) => typeof(ButtonBase).GetProperty("IsPressed")!
        .GetSetMethod(nonPublic: true)!.Invoke(button, [value]);
    private static void Invoke(Button button)
    {
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
    private static void AddResources(FrameworkElement view)
    {
        foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
    }
    private static void Layout(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void Render(FrameworkElement view, string state)
    {
        var directory = Environment.GetEnvironmentVariable("FOCUSAPP_FOCUS_START_QA_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, state + ".png")); encoder.Save(stream);
    }
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { error = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) throw new InvalidOperationException("Focus start interaction failed.", error);
    }
}
