using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class ExportRecordsModalViewModelTests
{
    [Fact]
    public void Open_UsesDefaultsAndKeepsOnlyOnePopupOpen()
    {
        var viewModel = new ExportRecordsModalViewModel();

        viewModel.Open();

        Assert.True(viewModel.IsOpen);
        Assert.Equal("本月", viewModel.SelectedTimeRange);
        Assert.Equal("Excel (.xlsx)", viewModel.SelectedFileFormat);
        Assert.Equal("专注记录、任务明细", viewModel.IncludedContentSummary);

        viewModel.ToggleTimeRangePopupCommand.Execute(null);
        Assert.True(viewModel.IsTimeRangePopupOpen);

        viewModel.ToggleFileFormatPopupCommand.Execute(null);
        Assert.False(viewModel.IsTimeRangePopupOpen);
        Assert.True(viewModel.IsFileFormatPopupOpen);

        viewModel.IsCsvSelected = true;
        Assert.Equal("CSV (.csv)", viewModel.SelectedFileFormat);
        Assert.Equal(ExportRecordsPopup.None, viewModel.ActivePopup);

        viewModel.ExportCommand.Execute(null);
        Assert.True(viewModel.IsOpen);

        viewModel.CloseCommand.Execute(null);
        Assert.False(viewModel.IsOpen);
    }

    [Fact]
    public void IncludedContent_UpdatesOnlyTheInMemorySummary()
    {
        var viewModel = new ExportRecordsModalViewModel();
        viewModel.Open();

        viewModel.ToggleIncludedContentPopupCommand.Execute(null);
        viewModel.IncludeTaskDetails = false;

        Assert.True(viewModel.IsIncludedContentPopupOpen);
        Assert.Equal("专注记录", viewModel.IncludedContentSummary);

        viewModel.IncludeFocusRecords = false;
        viewModel.CompleteIncludedContentCommand.Execute(null);

        Assert.Equal("未选择", viewModel.IncludedContentSummary);
        Assert.Equal(ExportRecordsPopup.None, viewModel.ActivePopup);
    }
}
