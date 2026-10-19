using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalIconPickerInteractionTests
{
    [Fact]
    public void IconAndColorClicksStayOpenAndOutsideClicksOnlyDismissThePicker()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var model = new StatisticsOverviewViewModel(useSampleData: false);
                model.AddGoalCommand.Execute(null);
                var modal = new CreateGoalModal { DataContext = model };
                foreach (var name in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/FocusApp.Desktop;component/Resources/{name}.xaml", UriKind.Relative) });
                host = new Window { Content = modal, Width = 920, Height = 710,
                    WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
                host.Show();
                Pump();
                var card = (Border)modal.FindName("DialogCard");
                var originalSize = new Size(card.ActualWidth, card.ActualHeight);
                Click((Button)modal.FindName("GoalIconLibraryButton"));
                Pump();
                var popup = (Popup)modal.FindName("GoalIconLibraryPopup");
                var library = (FrameworkElement)popup.Child;
                var brain = Descendants<Button>(library).Single(button => button.DataContext is TargetIconOptionViewModel { FileName: "brain.svg" });
                Click(brain);
                Pump();
                Assert.True(popup.IsOpen);
                Assert.Same(model.SelectedTargetIcon, model.QuickTargetIcons[0]);
                Assert.Equal("brain.svg", model.SelectedTargetIcon!.FileName);
                Assert.Single(Descendants<Button>(library).Where(button => Color(button.Background) == "#FFF1F2F4"));
                var colorChoices = (ItemsControl)modal.FindName("GoalColorChoices");
                var moreLabel = Assert.Single(Descendants<TextBlock>((Button)modal.FindName("GoalIconLibraryButton")));
                var moreColor = Color(moreLabel.Foreground);
                var purple = Descendants<Button>(colorChoices).Single(button => ReferenceEquals(button.DataContext, model.GoalColors[6]));
                Click(purple);
                Pump();
                Assert.True(popup.IsOpen);
                Assert.Equal("#A66AF3", model.SelectedGoalColorHex);
                Assert.EndsWith("#A66AF3", model.SelectedTargetIcon.DisplayIconSource);
                Assert.Single(Descendants<Ellipse>(colorChoices).Where(ellipse => ellipse.Name == "SelectionRing" && ellipse.Visibility == Visibility.Visible));
                var quick = Descendants<ItemsControl>(modal).Single(items => ReferenceEquals(items.ItemsSource, model.QuickTargetIcons));
                var first = Descendants<Button>(quick).First();
                Assert.Same(model.SelectedTargetIcon, first.DataContext);
                Assert.Equal("#FFF1F2F4", Color(first.Background));
                var quickImage = Assert.Single(Descendants<Image>(first));
                var libraryImage = Assert.Single(Descendants<Image>(brain));
                Assert.Same(quickImage.Source, libraryImage.Source);
                AssertIconTint(quick, "#FFA66AF3");
                AssertIconTint(library, "#FFA66AF3");
                Assert.Equal(moreColor, Color(moreLabel.Foreground));
                Assert.Equal(originalSize, new Size(card.ActualWidth, card.ActualHeight));

                var shortcutOrder = model.QuickTargetIcons.ToArray();
                var existing = model.QuickTargetIcons[2];
                Click(Descendants<Button>(library).Single(button =>
                    button.DataContext is TargetIconOptionViewModel icon && icon.Id == existing.Id));
                Pump();
                Assert.Equal(shortcutOrder, model.QuickTargetIcons);
                Assert.Equal(existing.Id, model.SelectedTargetIconId);
                Assert.True(popup.IsOpen);
                foreach (var choices in new[] { (FrameworkElement)quick, library })
                {
                    var selectedButton = Assert.Single(Descendants<Button>(choices).Where(button => Color(button.Background) == "#FFF1F2F4"));
                    Assert.Equal(existing.Id, Assert.IsType<TargetIconOptionViewModel>(selectedButton.DataContext).Id);
                }

                // Clicking a different part of the dialog dismisses only the picker.
                ((TextBox)modal.FindName("NewGoalNameTextBox")).RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Pump();
                Assert.False(popup.IsOpen);
                Assert.True(model.IsCreateGoalDialogOpen);
                Click((Button)modal.FindName("GoalIconLibraryButton"));
                Pump();
                var overlay = (Grid)modal.FindName("CreateGoalDialogOverlay");
                var outside = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent };
                overlay.RaiseEvent(outside);
                Pump();
                Assert.True(outside.Handled);
                Assert.False(popup.IsOpen);
                Assert.True(model.IsCreateGoalDialogOpen);
            }
            catch (Exception error) { failure = error; }
            finally { host?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw new InvalidOperationException("Goal icon picker interaction failed.", failure);
    }

    private static string? Color(Brush? brush) => (brush as SolidColorBrush)?.Color.ToString();
    private static void AssertIconTint(DependencyObject root, string expected)
    {
        foreach (var image in Descendants<Image>(root).Where(image => image.DataContext is TargetIconOptionViewModel))
        {
            var drawing = Assert.IsType<DrawingGroup>(Assert.IsType<DrawingImage>(image.Source).Drawing);
            var visible = drawing.Children.OfType<GeometryDrawing>().Where(shape =>
                shape.Brush is SolidColorBrush brush && brush.Color.A > 0).ToArray();
            Assert.NotEmpty(visible);
            Assert.All(visible, shape => Assert.Equal(expected, Color(shape.Brush)));
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Click(Button button)
    {
        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent });
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, index))) yield return child;
    }
}
