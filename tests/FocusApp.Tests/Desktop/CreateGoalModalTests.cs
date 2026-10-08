using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows.Threading;
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
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
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
    public void DialogShowsLightweightInputsAndPreservesOptionalFields()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new StatisticsOverviewViewModel();
                var modal = new CreateGoalModal { DataContext = viewModel };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                viewModel.AddGoalCommand.Execute(null);
                modal.Measure(new Size(800, 710));
                modal.Arrange(new Rect(0, 0, 800, 710));
                modal.UpdateLayout();
                var dialog = (Border)modal.FindName("DialogCard");
                var remark = (TextBox)modal.FindName("NewGoalRemarkTextBox");
                Assert.InRange(Math.Abs(310 - dialog.ActualWidth), 0, 0.5);
                Assert.InRange(Math.Abs(328 - dialog.ActualHeight), 0, 0.5);
                Assert.Equal(Visibility.Visible, remark.Visibility);
                Assert.Null(modal.FindName("ToggleGoalMoreButton"));
                Assert.Equal("创建新目标", viewModel.GoalDialogTitle);
                Assert.Equal("0/50", viewModel.NameCharacterCountDisplay);
                RenderState("collapsed");

                viewModel.NewGoalRemark = "保留备注";
                viewModel.SelectGoalColorCommand.Execute(viewModel.GoalColors[5]);
                Assert.Equal(7, viewModel.GoalColors.Count);
                Assert.Single(viewModel.GoalColors.Where(color => color.IsSelected));
                Assert.EndsWith("#299BFA", viewModel.SelectedTargetIcon!.DisplayIconSource);
                Assert.Equal("study.svg", FocusApp.Desktop.Services.TargetIconCatalog.ResolveIconFileName("study.png"));
                Assert.NotEmpty(viewModel.AllTargetIcons);
                Assert.Null(TargetIconSourceConverter.Instance.Convert(
                    "/FocusApp.Desktop;component/Assets/Icons/targetSelected_Svg/missing.svg#299BFA",
                    typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture));
                foreach (var icon in viewModel.AllTargetIcons)
                    Assert.IsType<DrawingImage>(new TargetIconSourceConverter().Convert(icon.DisplayIconSource,
                        typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture));
                var image = (DrawingImage)new TargetIconSourceConverter().Convert(
                    viewModel.SelectedTargetIcon.DisplayIconSource, typeof(ImageSource), null!, System.Globalization.CultureInfo.InvariantCulture)!;
                Assert.NotEmpty(((DrawingGroup)image.Drawing).Children);
                viewModel.NewGoalName = "阅读";
                RenderState("expanded");

                modal.UpdateLayout();
                Assert.InRange(Math.Abs(328 - dialog.ActualHeight), 0, 0.5);
                Assert.Equal("保留备注", viewModel.NewGoalRemark);
                viewModel.NewGoalName = new string('字', 51);
                Assert.Equal("50/50", viewModel.NameCharacterCountDisplay);
                viewModel.ConfirmCreateGoalCommand.Execute(null);
                var goal = viewModel.SelectedGoal!;
                Assert.Equal("#299BFA", goal.IconColorHex);
                Assert.EndsWith(".svg", goal.IconFileName);
                viewModel.EditGoalCommand.Execute(goal);
                Assert.Equal("#299BFA", viewModel.SelectedGoalColorHex);
                Assert.Equal("保留备注", viewModel.NewGoalRemark);

                void RenderState(string state)
                {
                    modal.UpdateLayout();
                    var output = Environment.GetEnvironmentVariable("FOCUSAPP_CREATE_GOAL_QA_DIR");
                    if (string.IsNullOrEmpty(output)) return;
                    Directory.CreateDirectory(output);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(modal);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"create-goal-{state}.png"));
                    encoder.Save(stream);
                    if (state == "collapsed")
                    {
                        var library = (Border)((Popup)modal.FindName("GoalIconLibraryPopup")).Child;
                        library.DataContext = viewModel;
                        library.Measure(new Size(344, 230));
                        library.Arrange(new Rect(0, 0, 344, 230));
                        library.UpdateLayout();
                        var drawing = new DrawingVisual();
                        using (var context = drawing.RenderOpen())
                        {
                            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(150, 150, 150)), null, new Rect(0, 0, 940, 710));
                            context.DrawRectangle(new VisualBrush(modal), null, new Rect(0, 0, 800, 710));
                            var cardPosition = dialog.TranslatePoint(new Point(), modal);
                            context.DrawRectangle(new VisualBrush(library), null, new Rect(cardPosition.X + dialog.ActualWidth + 10,
                                cardPosition.Y + (dialog.ActualHeight - 230) / 2, 344, 230));
                        }
                        var libraryPreview = new System.Windows.Media.Imaging.RenderTargetBitmap(940, 710, 96, 96, PixelFormats.Pbgra32);
                        libraryPreview.Render(drawing);
                        var libraryEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        libraryEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(libraryPreview));
                        using var libraryStream = File.Create(Path.Combine(output, "create-goal-icon-library.png"));
                        libraryEncoder.Save(libraryStream);
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void RemarkGrowsToThreeRealLinesThenScrollsAndKeepsNewlinesAndCharacterLimits()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = new StatisticsOverviewViewModel();
                var modal = new CreateGoalModal { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    modal.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                model.AddGoalCommand.Execute(null);
                var card = (Border)modal.FindName("DialogCard");
                var name = (TextBox)modal.FindName("NewGoalNameTextBox");
                var remark = (TextBox)modal.FindName("NewGoalRemarkTextBox");
                Layout(); Assert.Equal(328, model.GoalDialogHeight); Assert.Equal(20, remark.Height);
                Assert.Equal(new Thickness(0), name.BorderThickness); Assert.Equal(new Thickness(0), remark.BorderThickness);
                Assert.True(remark.AcceptsReturn); Assert.Equal(TextWrapping.Wrap, remark.TextWrapping);
                model.NewGoalName = "学习英语";
                remark.Text = "每天至少学习30分钟。"; Layout(); Assert.Equal(328, model.GoalDialogHeight);
                remark.AppendText("\r\n"); Layout();
                Assert.EndsWith("\r\n", model.NewGoalRemark); Assert.Equal(348, model.GoalDialogHeight);
                remark.AppendText("重点练习口语和听力。\r\n坚持90天。"); Layout();
                Assert.Equal(368, model.GoalDialogHeight); Assert.Equal(60, remark.Height);
                remark.AppendText("\r\n第四行在输入区内部滚动。"); Layout();
                Assert.Equal(368, model.GoalDialogHeight);
                var scroll = Descendants(remark).OfType<ScrollViewer>().Single();
                Assert.True(scroll.ScrollableHeight > 0); Assert.InRange(scroll.ViewportHeight, 59, 61);
                var output = Environment.GetEnvironmentVariable("FOCUSAPP_CREATE_GOAL_QA_DIR");
                Save("three-lines-scroll");
                model.NewGoalRemark = new string('字', 151); Layout();
                Assert.Equal(150, model.NewGoalRemark.Length); Assert.Equal("150/150", model.RemarkCharacterCountDisplay);
                Assert.True(model.IsGoalRemarkAtLimit); Assert.True(model.CanCreateGoal);
                Assert.True(remark.LineCount > 3); Assert.True(scroll.ScrollableHeight > 0); Assert.Equal(368, model.GoalDialogHeight);
                Save("character-limit");
                model.NewGoalRemark = "单行备注"; Layout(); Assert.Equal(328, model.GoalDialogHeight);
                model.NewGoalRemark = "第一行\r\n第二行\r\n第三行"; Layout(); Save("three-lines");
                model.ConfirmCreateGoalCommand.Execute(null);
                var goal = model.SelectedGoal!; Assert.Equal("第一行\r\n第二行\r\n第三行", goal.Remark);
                model.EditGoalCommand.Execute(goal); Layout(); Assert.Equal(368, model.GoalDialogHeight);
                Assert.Equal(goal.Remark, remark.Text);
                model.CancelCreateGoalCommand.Execute(null); model.AddGoalCommand.Execute(null); Layout();
                Assert.Equal(328, model.GoalDialogHeight); Assert.Equal(string.Empty, model.NewGoalRemark);
                Save("empty-lightweight");
                void Layout()
                {
                    for (var pass = 0; pass < 3; pass++)
                    {
                        modal.Measure(new Size(800, 710)); modal.Arrange(new Rect(0, 0, 800, 710)); modal.UpdateLayout();
                        modal.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    }
                    Assert.InRange(Math.Abs(card.ActualWidth - 310), 0, 0.5);
                    Assert.InRange(Math.Abs(card.ActualHeight - model.GoalDialogHeight), 0, 0.5);
                }
                void Save(string state)
                {
                    if (string.IsNullOrEmpty(output)) return;
                    Directory.CreateDirectory(output);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(800, 710, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(modal);
                    var origin = card.TranslatePoint(new Point(), modal);
                    var crop = new System.Windows.Media.Imaging.CroppedBitmap(bitmap, new Int32Rect((int)Math.Round(origin.X), (int)Math.Round(origin.Y), (int)Math.Round(card.ActualWidth), (int)Math.Round(card.ActualHeight)));
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));
                    using var stream = File.Create(Path.Combine(output, $"create-goal-{state}.png")); encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20))); Assert.Null(failure);
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
        var rootGrid = window.Root!.Element(p + "Grid")!.Element(p + "Border")!.Element(p + "Grid")!;
        var modal = Assert.Single(rootGrid.Elements(v + "CreateGoalModal"));
        Assert.Equal("2", (string?)modal.Attribute("Grid.ColumnSpan"));
        Assert.Equal("{Binding StatisticsPage}", (string?)modal.Attribute("DataContext"));
        Assert.Equal("-1", (string?)modal.Attribute("Margin"));
        var modalLayer = int.Parse(modal.Attribute("Panel.ZIndex")!.Value);
        Assert.All(rootGrid.Elements().Where(e => e != modal), e =>
            Assert.True(int.Parse((string?)e.Attribute("Panel.ZIndex") ?? "0") < modalLayer));
    }

    private static XName XName(string localName) =>
        XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + localName;

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
