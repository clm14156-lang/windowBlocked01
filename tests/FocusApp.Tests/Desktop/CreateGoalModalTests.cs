using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CreateGoalModalTests
{
    internal static void VerifyButtonLabelsWithApplicationTextStyles()
    {
                var viewModel = new StatisticsOverviewViewModel();
                var modal = new CreateGoalModal { DataContext = viewModel };
                viewModel.AddGoalCommand.Execute(null);
                modal.Measure(new Size(800, 710));
                modal.Arrange(new Rect(0, 0, 800, 710));
                modal.UpdateLayout();

                var output = Environment.GetEnvironmentVariable("FOCUSAPP_MODAL_COLOR_QA_PATH");
                if (!string.IsNullOrEmpty(output))
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        800, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(modal);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(output);
                    encoder.Save(stream);
                }
                var labels = Descendants(modal).OfType<TextBlock>().ToArray();
                var create = Assert.Single(labels.Where(label => label.Text == "创建"));
                var cancel = Assert.Single(labels.Where(label => label.Text == "取消"));
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(create.Foreground).Color);
                Assert.Equal(((SolidColorBrush)modal.FindResource("TextSecondary")).Color,
                    Assert.IsType<SolidColorBrush>(cancel.Foreground).Color);
                viewModel.CancelCreateGoalCommand.Execute(null);
                var goal = viewModel.Goals.First();
                viewModel.EditGoalCommand.Execute(goal);
                modal.UpdateLayout();
                var editLabels = Descendants(modal).OfType<TextBlock>().ToArray();
                Assert.Contains(editLabels, label => label.Text == "编辑目标");
                var save = Assert.Single(editLabels.Where(label => label.Text == "保存"));
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(save.Foreground).Color);
                Assert.Equal(goal.Name, ((TextBox)modal.FindName("NewGoalNameTextBox")).Text);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Fact]
    public void IconLibraryPlacementUsesDialogSidesWithoutOverlappingIt()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var modal = new CreateGoalModal();
                var popup = (Popup)modal.FindName("GoalIconLibraryPopup");
                var placements = popup.CustomPopupPlacementCallback!(
                    new Size(344, 230), new Size(340, 280), default);

                Assert.Equal(2, placements.Length);
                Assert.Equal(new Point(350, 25), placements[0].Point);
                Assert.Equal(new Point(-354, 25), placements[1].Point);
                Assert.Equal(PopupPrimaryAxis.Horizontal, placements[0].PrimaryAxis);
                Assert.Equal(PopupPrimaryAxis.Horizontal, placements[1].PrimaryAxis);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void ModalIsHostedAboveBothMainWindowColumns()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusApp.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var window = XDocument.Load(Path.Combine(directory.FullName, "src", "FocusApp.Desktop", "MainWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace v = "clr-namespace:FocusApp.Desktop.Views";
        var rootGrid = window.Root!.Element(p + "Border")!.Element(p + "Grid")!;
        var modal = Assert.Single(rootGrid.Elements(v + "CreateGoalModal"));
        Assert.Equal("2", (string?)modal.Attribute("Grid.ColumnSpan"));
        Assert.Equal("{Binding StatisticsPage}", (string?)modal.Attribute("DataContext"));
        Assert.Equal("-1", (string?)modal.Attribute("Margin"));
        var modalLayer = int.Parse(modal.Attribute("Panel.ZIndex")!.Value);
        Assert.All(rootGrid.Elements().Where(e => e != modal), e =>
            Assert.True(int.Parse((string?)e.Attribute("Panel.ZIndex") ?? "0") < modalLayer));
    }

    [Theory]
    [InlineData(800, 710, 1)]
    [InlineData(1024, 768, 1.25)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(3840, 2160, 2)]
    public void BackdropFillsLayoutAndOnlyOutsideClicksDismiss(double width, double height, double scale)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new StatisticsOverviewViewModel();
                var modal = new CreateGoalModal { DataContext = viewModel };
                VisualTreeHelper.SetRootDpi(modal, new DpiScale(scale, scale));
                viewModel.AddGoalCommand.Execute(null);
                // WPF lays out in device-independent units at each display scale.
                var size = new Size(width / scale, height / scale);
                modal.Measure(size);
                modal.Arrange(new Rect(size));
                modal.UpdateLayout();
                var overlay = (Grid)modal.FindName("CreateGoalDialogOverlay");
                var card = (Border)modal.FindName("DialogCard");
                // Layout rounding may adjust fractional DIPs by less than one pixel.
                Assert.InRange(Math.Abs(size.Width - overlay.ActualWidth), 0, 1 / scale);
                Assert.InRange(Math.Abs(size.Height - overlay.ActualHeight), 0, 1 / scale);
                foreach (var point in new[] { new Point(1, 1), new Point(size.Width - 1, 1),
                             new Point(1, size.Height - 1), new Point(size.Width - 1, size.Height - 1) })
                    Assert.Same(overlay, VisualTreeHelper.HitTest(modal, point)!.VisualHit);

                card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseDownEvent });
                Assert.True(viewModel.IsCreateGoalDialogOpen);
                viewModel.NewGoalName = "不要保存";
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.MouseDownEvent };
                overlay.RaiseEvent(click);
                Assert.True(click.Handled);
                Assert.False(viewModel.IsCreateGoalDialogOpen);
                Assert.Empty(viewModel.NewGoalName);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
