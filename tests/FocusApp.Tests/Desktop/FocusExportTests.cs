using ClosedXML.Excel;
using FocusApp.Contracts;
using FocusApp.Desktop.Services;
using FocusApp.Desktop.ViewModels;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class FocusExportDataServiceTests
{
    [Fact]
    public void Build_UsesSessionAsPrimaryRowAndPreservesHistoricalRelationships()
    {
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 29));
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, localOffset);
        var target = new LocalTargetDto("goal-1", "归档目标", true, 0, now, now);
        var sessionId = Guid.NewGuid();
        var session = CompletedSession(
            sessionId,
            now.AddHours(-1),
            now,
            3600,
            target.TargetId,
            targetNameSnapshot: target.Name,
            [
                new LocalFocusSessionTaskSnapshotDto("task-2", "第二个任务", 1),
                new LocalFocusSessionTaskSnapshotDto("task-1", "第一个任务", 0)
            ]);
        var snapshot = Snapshot([session], [target]);

        var data = new FocusExportDataService().Build(
            snapshot,
            FocusExportTimeRange.AllRecords,
            now.LocalDateTime);

        var record = Assert.Single(data.FocusRecords);
        Assert.Equal(sessionId, record.SessionId);
        Assert.Equal(new DateOnly(2026, 8, 29), record.Date);
        Assert.Equal(60, record.DurationMinutes);
        Assert.Equal("归档目标", record.GoalName);
        Assert.Equal(["第一个任务", "第二个任务"], data.TaskRecords.Select(item => item.TaskName));
        Assert.All(data.TaskRecords, item => Assert.Equal(sessionId, item.SessionId));
        var summary = Assert.Single(data.DailySummaries);
        Assert.Equal(60, summary.DurationMinutes);
        Assert.Equal(1, summary.FocusCount);
        Assert.Equal(2, summary.CompletedTaskCount);
    }

    [Fact]
    public void Build_LeavesGoalBlankAndHandlesCrossMidnightAndCurrentMonthBoundary()
    {
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 29));
        var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, localOffset);
        var inMonth = CompletedSession(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 28, 23, 37, 0, localOffset),
            new DateTimeOffset(2026, 8, 29, 0, 2, 0, localOffset),
            1500,
            null,
            null,
            []);
        var previousMonth = CompletedSession(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 7, 31, 23, 0, 0, localOffset),
            new DateTimeOffset(2026, 7, 31, 23, 30, 0, localOffset),
            1800,
            null,
            null,
            []);
        var snapshot = Snapshot([inMonth, previousMonth], []);
        var service = new FocusExportDataService();

        var month = service.Build(snapshot, FocusExportTimeRange.CurrentMonth, now.LocalDateTime);
        var record = Assert.Single(month.FocusRecords);
        Assert.Null(record.GoalName);
        Assert.Equal(25, record.DurationMinutes);
        Assert.Equal(new DateOnly(2026, 8, 28), record.Date);

        var all = service.Build(snapshot, FocusExportTimeRange.AllRecords, now.LocalDateTime);
        Assert.Equal(2, all.FocusRecords.Count);
        Assert.Equal(55, all.DailySummaries.Sum(item => item.DurationMinutes));
    }

    private static LocalFocusSessionDto CompletedSession(
        Guid id,
        DateTimeOffset start,
        DateTimeOffset end,
        int actualSeconds,
        string? targetId,
        string? targetNameSnapshot,
        IReadOnlyList<LocalFocusSessionTaskSnapshotDto> tasks) =>
        new(
            id,
            LocalFocusSessionStatusDto.Completed,
            false,
            actualSeconds,
            actualSeconds,
            start.AddSeconds(-5),
            start,
            end,
            end,
            FocusCompletionKindDto.Natural,
            targetId,
            targetNameSnapshot,
            false,
            null,
            null,
            tasks);

    private static LocalDataSnapshotDto Snapshot(
        IReadOnlyList<LocalFocusSessionDto> sessions,
        IReadOnlyList<LocalTargetDto> targets) =>
        new(
            1,
            sessions,
            targets,
            [],
            [],
            [],
            [],
            new LocalAppSettingsDto(false, true, true, true, false, false, "Orange", null, DateTimeOffset.UtcNow),
            [],
            []);
}

public sealed class ExcelExportServiceTests
{
    [Fact]
    public void Save_CreatesThreeSheetsAndWritesOneRowPerCompletedTask()
    {
        var path = Path.Combine(Path.GetTempPath(), $"focus-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            var sessionId = Guid.NewGuid();
            var date = new DateOnly(2026, 8, 29);
            var data = new FocusExportData(
                [new FocusExportRecord(sessionId, date, new DateTime(2026, 8, 29, 9, 0, 0), new DateTime(2026, 8, 29, 10, 0, 0), 60, "学习")],
                [
                    new FocusTaskExportRecord(sessionId, date, "学习", "任务一"),
                    new FocusTaskExportRecord(sessionId, date, "学习", "任务二"),
                    new FocusTaskExportRecord(sessionId, date, "学习", "任务三")
                ],
                [new FocusDailyExportSummary(date, 60, 1, 3)]);

            new ExcelExportService().Save(
                path,
                data,
                new FocusExportOptions(FocusExportTimeRange.AllRecords, true, true));

            using var workbook = new XLWorkbook(path);
            Assert.Equal(["专注记录", "任务明细", "每日汇总"], workbook.Worksheets.Select(sheet => sheet.Name));
            Assert.Equal(2, workbook.Worksheet("专注记录").LastRowUsed()!.RowNumber());
            var taskSheet = workbook.Worksheet("任务明细");
            Assert.Equal(4, taskSheet.LastRowUsed()!.RowNumber());
            Assert.All(taskSheet.Rows(2, 4), row => Assert.Equal(sessionId.ToString(), row.Cell(1).GetString()));
            Assert.Equal(["任务一", "任务二", "任务三"], taskSheet.Rows(2, 4).Select(row => row.Cell(4).GetString()));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Save_CreatesTypedSheetsAndHonorsContentSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"focus-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            var sessionId = Guid.NewGuid();
            var data = new FocusExportData(
                [new FocusExportRecord(sessionId, new DateOnly(2026, 8, 28), new DateTime(2026, 8, 28, 13, 0, 0), new DateTime(2026, 8, 28, 14, 0, 0), 60, null)],
                [new FocusTaskExportRecord(sessionId, new DateOnly(2026, 8, 28), "目标", "完成任务")],
                [new FocusDailyExportSummary(new DateOnly(2026, 8, 28), 60, 1, 1)]);

            new ExcelExportService().Save(
                path,
                data,
                new FocusExportOptions(FocusExportTimeRange.AllRecords, true, false));

            using var workbook = new XLWorkbook(path);
            Assert.Equal(["专注记录", "每日汇总"], workbook.Worksheets.Select(sheet => sheet.Name));
            var sheet = workbook.Worksheet("专注记录");
            Assert.Equal(sessionId.ToString(), sheet.Cell(2, 1).GetString());
            Assert.Equal("2026-08-28", sheet.Cell(2, 2).GetDateTime().ToString("yyyy-MM-dd"));
            Assert.Equal(new TimeSpan(13, 0, 0), sheet.Cell(2, 3).GetTimeSpan());
            Assert.Equal(60, sheet.Cell(2, 5).GetValue<int>());
            Assert.True(sheet.Cell(2, 6).IsEmpty());
            Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 2).DataType);
            Assert.Equal(XLDataType.TimeSpan, sheet.Cell(2, 3).DataType);
            Assert.Equal(XLDataType.Number, sheet.Cell(2, 5).DataType);
            Assert.Equal("yyyy-mm-dd", sheet.Cell(2, 2).Style.DateFormat.Format);
            Assert.Equal("hh:mm", sheet.Cell(2, 3).Style.DateFormat.Format);
            Assert.Equal(2, sheet.AutoFilter.Range.RowCount());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

public sealed class ExportRecordsModalExporterTests
{
    [Fact]
    public async Task ExportCommand_ShowsSuccessAndPreventsEmptySelection()
    {
        var exporter = new FakeExporter(new FocusExportResult(FocusExportResultKind.Success));
        var viewModel = new ExportRecordsModalViewModel(exporter);
        viewModel.Open();
        viewModel.ExportCommand.Execute(null);
        await exporter.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(25);

        Assert.Equal(ExportNotificationKind.Success, viewModel.NotificationKind);
        Assert.Equal("导出成功", viewModel.NotificationTitle);
        Assert.False(viewModel.IsOpen);

        viewModel.Open();
        viewModel.IncludeFocusRecords = false;
        viewModel.IncludeTaskDetails = false;
        Assert.False(viewModel.CanExport);
    }

    [Fact]
    public async Task ExportCommand_ShowsNoDataAndFailureWithoutClosingModal()
    {
        var noData = new FakeExporter(new FocusExportResult(FocusExportResultKind.NoData));
        var viewModel = new ExportRecordsModalViewModel(noData);
        viewModel.Open();
        viewModel.ExportCommand.Execute(null);
        await noData.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(25);
        Assert.Equal("暂无可导出记录", viewModel.NotificationTitle);
        Assert.True(viewModel.IsOpen);

        var failure = new FakeExporter(new FocusExportResult(FocusExportResultKind.Failure, "无法写入"));
        viewModel = new ExportRecordsModalViewModel(failure);
        viewModel.Open();
        viewModel.ExportCommand.Execute(null);
        await failure.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(25);
        Assert.Equal(ExportNotificationKind.Failure, viewModel.NotificationKind);
        Assert.Equal("无法写入", viewModel.NotificationMessage);
        Assert.True(viewModel.IsOpen);
    }

    private sealed class FakeExporter(FocusExportResult result) : IFocusRecordsExporter
    {
        public TaskCompletionSource<bool> Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FocusExportResult> ExportAsync(FocusExportOptions options, CancellationToken cancellationToken = default)
        {
            Called.TrySetResult(true);
            return Task.FromResult(result);
        }
    }
}
