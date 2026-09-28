using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusApp.Desktop.ViewModels;
using FocusApp.Desktop.Views;
using Xunit;

namespace FocusApp.Tests.Desktop;

[Collection("Calendar UI")]
public sealed class GoalEmptyStatePresentationTests
{
    [Fact]
    public void CurrentGoalGuideHasOneCreationEntryAndUpdatesWhenGoalsAreArchivedOrRestored()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var model = new StatisticsOverviewViewModel(false);
                model.SelectGoalsCommand.Execute(null);
                var page = new StatisticsPage { DataContext = model };
                foreach (var resource in new[] { "Colors", "Typography", "Strings", "Styles" })
                    page.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/FocusApp.Desktop;component/Resources/{resource}.xaml", UriKind.Relative) });
                window = new Window { Width = 726, Height = 676, Content = page, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false, Left = -32000, Top = -32000 };
                window.Show();
                Pump();
                var left = (Border)page.FindName("GoalListPanel");
                var right = (Border)page.FindName("GoalInvestmentDetailsCard");
                var leftEmpty = (FrameworkElement)page.FindName("GoalListEmptyState");
                var guide = (FrameworkElement)page.FindName("GoalFirstCreationGuide");
                var existing = (FrameworkElement)page.FindName("GoalExistingContent");
                var add = (Button)page.FindName("EmptyGoalCreateButton");
                var headerAdd = (Button)page.FindName("GoalListAddButton");
                Assert.Equal(190, left.ActualWidth);
                Assert.Equal(604, right.ActualHeight);
                var rightWidth = right.ActualWidth;
                Assert.True(model.IsGoalCreationGuideVisible);
                Assert.True(leftEmpty.IsVisible);
                Assert.True(guide.IsVisible);
                Assert.False(existing.IsVisible); // Guests also see the guide, rather than a VIP placeholder.
                Assert.False(headerAdd.IsVisible);
                Assert.Single(Descendants<Button>(page).Where(button => button.IsVisible && button.Command == model.AddGoalCommand));
                Assert.Empty(Descendants<Button>(guide));
                add.Command.Execute(add.CommandParameter);
                Assert.True(model.IsCreateGoalDialogOpen);
                model.NewGoalName = "创建我的第一个目标";
                Assert.True(model.ConfirmCreateGoalCommand.CanExecute(null));
                model.ConfirmCreateGoalCommand.Execute(null);
                Pump();
                Assert.Single(model.Goals);
                Assert.False(model.IsGoalCreationGuideVisible);
                Assert.False(leftEmpty.IsVisible);
                Assert.False(guide.IsVisible);
                Assert.True(existing.IsVisible);
                Assert.True(headerAdd.IsVisible);
                Assert.Equal(rightWidth, right.ActualWidth);
                Assert.Equal(604, right.ActualHeight);

                var state = GoalNextTaskDetailsTests.State();
                model.ApplyState(state with { Targets = [state.Targets[0] with { IsArchived = true }] });
                Pump();
                Assert.Empty(model.VisibleGoals);
                Assert.True(model.IsGoalCreationGuideVisible);
                Assert.True(guide.IsVisible);
                Assert.True(leftEmpty.IsVisible);
                Assert.False(headerAdd.IsVisible);
                Assert.Single(Descendants<Button>(page).Where(button => button.IsVisible && button.Command == model.AddGoalCommand));
                model.SelectGoalListCommand.Execute("Archived");
                Pump();
                Assert.Single(model.VisibleGoals);
                Assert.False(guide.IsVisible);
                Assert.False(leftEmpty.IsVisible);
                Assert.True(headerAdd.IsVisible);

                var archivedGoal = Assert.Single(model.Goals);
                model.RestoreGoalCommand.Execute(archivedGoal);
                model.SelectGoalListCommand.Execute("Current");
                Pump();
                Assert.Single(model.VisibleGoals);
                Assert.False(guide.IsVisible);
                model.ArchiveGoalCommand.Execute(archivedGoal);
                Pump();
                Assert.Empty(model.VisibleGoals);
                Assert.True(guide.IsVisible);
                Assert.True(leftEmpty.IsVisible);
                Assert.False(headerAdd.IsVisible);
                model.RestoreGoalCommand.Execute(archivedGoal);
                Pump();
                Assert.False(guide.IsVisible);
                Assert.True(existing.IsVisible);
                Assert.True(headerAdd.IsVisible);

                model.ApplyState(state with { Targets = [] });
                Pump();
                Assert.True(guide.IsVisible);
                Assert.True(leftEmpty.IsVisible);
                Assert.False(headerAdd.IsVisible);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Empty goal presentation verification timed out.");
        if (failure is not null) throw new InvalidOperationException("Empty goal presentation failed.", failure);
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
