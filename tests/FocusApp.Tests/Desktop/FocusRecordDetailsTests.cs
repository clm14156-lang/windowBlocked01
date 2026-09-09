using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public class FocusRecordDetailsTests
{
    [Fact]
    public void DetailVariantsMeasureAndRenderWithLongTaskNames()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var variant in new[] { 0, 1, 2 })
                {
                    var model = new StatisticsOverviewViewModel(useSampleData: false);
                    var record = new FocusSessionRecordViewModel(DateTime.Today.AddHours(20), DateTime.Today.AddHours(21),
                        variant == 0 ? "goal-unassigned" : "goal-test", "学习 UE5", "完成界面草图与交互细节，检查不同缩放比例下的文字布局", variant == 2 ? 3 : 0);
                    var window = new FocusApp.Desktop.Views.FocusRecordDetailsWindow(model, record);
                    var content = (System.Windows.FrameworkElement)window.Content;
                    content.Measure(new System.Windows.Size(340, double.PositiveInfinity));
                    content.Arrange(new System.Windows.Rect(0, 0, 340, content.DesiredSize.Height));
                    content.UpdateLayout();
                    Assert.InRange(content.ActualHeight, 150, 700);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(510, (int)Math.Ceiling(content.ActualHeight * 1.5), 144, 144, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"focus-detail-{variant}.png"));
                    encoder.Save(output);
                    window.Close();
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        Assert.Null(failure);
    }
    [Theory]
    [InlineData("goal-unassigned", false, false)]
    [InlineData("goal-test", false, false)]
    [InlineData("goal-test", true, true)]
    public void DetailsOnlyShowTasksForBoundGoals(string goalId, bool tasks, bool expected)
    {
        var record = new FocusSessionRecordViewModel(DateTime.Today, DateTime.Today.AddMinutes(25),
            goalId, "学习", tasks ? "完成阅读" : "", tasks ? 1 : 0);
        Assert.Equal(expected, record.ShowDetailTasks);
        Assert.Equal(goalId == "goal-unassigned" ? "自由专注" : "学习", record.CalendarTitle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersistentDeletionOnlyRemovesRecordAfterSuccess(bool success)
    {
        var model = new StatisticsOverviewViewModel(useSampleData: false);
        var id = Guid.NewGuid();
        var record = new FocusSessionRecordViewModel(DateTime.Today, DateTime.Today.AddMinutes(25),
            "goal-unassigned", "其他", "", 0) { SessionId = id };
        model.FocusSessionRecords.Add(record);
        model.PersistRecordDeletion = received => { Assert.Equal(id, received); return Task.FromResult(success); };
        Assert.Equal(success, await model.DeleteFocusRecordAsync(record));
        Assert.Equal(!success, model.FocusSessionRecords.Contains(record));
    }
}
