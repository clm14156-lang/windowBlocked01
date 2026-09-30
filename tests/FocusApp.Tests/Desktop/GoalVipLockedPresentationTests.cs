using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalVipLockedPresentationTests
{
    [Fact]
    public void LockedGoalTracksSelectionKeepsPanelsAndOpensExistingVipPurchase()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                var first = new GoalOverviewItemViewModel("first", "window屏蔽软件", "", "", false, false,
                    createdAtUtc: new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(8)), remark: "不应混入创建日期");
                var second = new GoalOverviewItemViewModel("second", "学习 UE5", "", "", false, false,
                    createdAtUtc: new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.FromHours(8)));
                model.Goals.Add(first);
                model.Goals.Add(second);
                model.SelectGoalsCommand.Execute(null);
                model.SelectGoalCommand.Execute(first);
                var home = new NavigationItemViewModel(NavigationPage.Home, "首页", "H");
                var root = new MainWindowViewModel([home], new NavigationItemViewModel(NavigationPage.Account, "账户", "A"),
                    new HomePageViewModel([new HomeDurationOptionViewModel("25 分钟", "", true, 25)]), statisticsPage: model);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 726, Height = 676, Content = page, DataContext = root, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var left = (Border)page.FindName("GoalListPanel");
                var right = (Border)page.FindName("GoalInvestmentDetailsCard");
                var locked = (Grid)page.FindName("GoalVipLockedContent");
                var unlocked = (Grid)page.FindName("GoalInvestmentDetailsContent");
                var guide = (StackPanel)page.FindName("GoalFirstCreationGuide");
                var name = (TextBlock)page.FindName("GoalVipSelectedName");
                var date = (TextBlock)page.FindName("GoalVipCreatedDate");
                var image = (Image)page.FindName("GoalVipIllustration");
                var features = (Grid)page.FindName("GoalVipFeatures");
                var body = (StackPanel)page.FindName("GoalVipLockedBody");
                var button = (Button)page.FindName("GoalVipUnlockButton");
                var width = right.ActualWidth;
                Assert.Equal(190, left.ActualWidth);
                Assert.InRange(left.ActualHeight, page.ActualHeight - 64, page.ActualHeight - 62);
                Assert.InRange(right.ActualHeight, page.ActualHeight - 64, page.ActualHeight - 62);
                Assert.True(locked.IsVisible);
                Assert.False(unlocked.IsVisible);
                Assert.False(guide.IsVisible);
                Assert.Equal(first.Name, name.Text);
                Assert.Equal(first.CreatedDateDisplay, date.Text);
                Assert.DoesNotContain("不应混入", date.Text);
                Assert.IsAssignableFrom<BitmapSource>(image.Source);
                Assert.Equal(Stretch.Uniform, image.Stretch);
                Assert.Null(page.FindName("GoalInvestmentDetailsVipGuidePopup"));
                Assert.Null(page.FindName("GoalInvestmentDetailsVipHoverTarget"));
                Assert.Single(Descendants<Button>(locked));
                Assert.Equal(4, features.Children.OfType<StackPanel>().Count());
                Assert.True(body.TranslatePoint(new Point(0, body.ActualHeight), right).Y <= right.ActualHeight - 25);
                foreach (var feature in features.Children.OfType<StackPanel>())
                {
                    Assert.True(feature.TranslatePoint(new Point(feature.ActualWidth, feature.ActualHeight), features).X <= features.ActualWidth);
                    Assert.True(feature.ActualHeight <= features.ActualHeight);
                }
                Assert.True(first.IsSelected);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                Assert.True(root.VipModal.IsOpen);
                root.VipModal.CloseCommand.Execute(null);

                model.SelectGoalCommand.Execute(second);
                Pump();
                Assert.Equal(second.Name, name.Text);
                Assert.Equal(second.CreatedDateDisplay, date.Text);
                Assert.True(second.IsSelected);
                Assert.False(first.IsSelected);
                second.Name = "学习 UE5 关卡设计";
                Pump();
                Assert.Equal(second.Name, name.Text);
                model.SetUserAccess(true, false);
                Pump();
                Assert.True(locked.IsVisible);
                model.SetUserAccess(true, true);
                Pump();
                Assert.False(locked.IsVisible);
                Assert.True(unlocked.IsVisible);
                Assert.Equal(width, right.ActualWidth);
                Assert.Equal(190, left.ActualWidth);
                model.SetUserAccess(false, false);
                Pump();
                Assert.True(locked.IsVisible);
                model.Goals.Clear();
                model.SelectGoalCommand.Execute(null);
                Pump();
                Assert.True(guide.IsVisible);
                Assert.False(locked.IsVisible);
                Assert.False(button.IsVisible);
                Assert.Equal(width, right.ActualWidth);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Goal VIP presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Goal VIP presentation failed.", failure);
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element) yield return element;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
