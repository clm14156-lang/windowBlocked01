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
    public void InputAndClicksRenderOneActiveButtonAndAllPointerStatesKeepTheCardFixed()
    {
        RunSta(() =>
        {
            var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", true, 60),
                new("90", "", true, 90), new("180", "", true, 180)]);
            var model = home.CustomTimeModal;
            model.Open();
            var modal = new CustomTimeModal { DataContext = model };
            AddResources(modal);
            Layout(modal, 320, 420);
            var card = (Border)modal.FindName("CustomTimeCard");
            var items = (ItemsControl)modal.FindName("CommonTimeOptions");
            var buttons = Descendants<Button>(items).Where(button => Equals(button.Command, model.SelectTimeCommand)).ToArray();
            Assert.Equal(4, buttons.Length);
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
            Assert.Equal("30", ((TextBox)modal.FindName("MinutesTextBox")).Text);
            AssertActive(first);
            Invoke(buttons[1]); Layout(modal, 320, 420);
            AssertActive(buttons[1]);
            Assert.Equal(Color.FromRgb(229, 229, 234), BrushColor(first.BorderBrush));
            Assert.Single(buttons.Where(button => button.Tag is true));
            var input = (TextBox)modal.FindName("MinutesTextBox");
            input.Text = "90"; Layout(modal, 320, 420);
            AssertActive(buttons[2]);
            Assert.Single(buttons.Where(button => button.Tag is true));
            Render(modal, "custom-time-active");
            input.Text = "45"; Layout(modal, 320, 420);
            Assert.All(buttons, button => Assert.Equal(Color.FromRgb(229, 229, 234), BrushColor(button.BorderBrush)));
            Assert.Empty(buttons.Where(button => button.Tag is true));
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
