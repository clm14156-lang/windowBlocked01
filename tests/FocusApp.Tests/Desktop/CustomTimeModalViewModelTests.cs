using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class CustomTimeModalViewModelTests
{
    [Fact]
    public void QuickSelectionIsSingleAndFollowsInputRatherThanHomeVisibility()
    {
        var home = new HomePageViewModel([new("30", "", true, 30), new("60", "", true, 60),
            new("90", "", true, 90), new("180", "", true, 180)]);
        var modal = home.CustomTimeModal;
        var notifications = new List<string?>();
        modal.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        modal.Open();
        Assert.Null(modal.SelectedTime);
        var thirty = modal.CommonTimes.Single(option => option.Minutes == 30);
        var sixty = modal.CommonTimes.Single(option => option.Minutes == 60);
        modal.SelectTimeCommand.Execute(thirty);
        Assert.Equal("30", modal.MinutesInput);
        Assert.Same(thirty, modal.SelectedTime);
        modal.SelectTimeCommand.Execute(sixty);
        Assert.Equal("60", modal.MinutesInput);
        Assert.Same(sixty, modal.SelectedTime);
        modal.SelectTimeCommand.Execute(sixty);
        Assert.Same(sixty, modal.SelectedTime);
        modal.MinutesInput = "90";
        Assert.Equal(90, modal.SelectedTime!.Minutes);
        modal.MinutesInput = "45";
        Assert.Null(modal.SelectedTime);
        modal.MinutesInput = "";
        Assert.Null(modal.SelectedTime);
        Assert.False(modal.ConfirmCommand.CanExecute(null));
        Assert.Contains(nameof(modal.SelectedTime), notifications);
    }

    [Fact]
    public void EditingDeletingAndReopeningDoNotLeaveAnActivePresetBehind()
    {
        var option = new HomeDurationOptionViewModel("60", "", true, 60);
        var modal = new CustomTimeModalViewModel(_ => true, [option]);
        modal.Open();
        modal.MinutesInput = "60";
        modal.ToggleEditCommand.Execute(null);
        modal.SelectTimeCommand.Execute(new HomeDurationOptionViewModel("90", "", false, 90));
        Assert.Equal(60, modal.Minutes);
        modal.DeleteTimeCommand.Execute(option);
        Assert.Null(modal.SelectedTime);
        modal.CompleteEditCommand.Execute(null);
        modal.CommonTimes.Add(option);
        Assert.Same(option, modal.SelectedTime);
        modal.Open();
        Assert.Equal(5, modal.Minutes);
        Assert.Null(modal.SelectedTime);
    }
}
