using ClosedXML.Excel;

namespace FocusApp.Desktop.Services;

public sealed class ExcelExportService
{
    private const string HeaderBackground = "#F2F2F7";

    public void Save(string path, FocusExportData data, FocusExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IncludeFocusRecords && !options.IncludeTaskDetails)
        {
            throw new ArgumentException("至少需要选择一项导出内容。", nameof(options));
        }

        using var workbook = new XLWorkbook();
        if (options.IncludeFocusRecords)
        {
            AddFocusRecordsSheet(workbook, data.FocusRecords);
        }

        if (options.IncludeTaskDetails)
        {
            AddTaskDetailsSheet(workbook, data.TaskRecords);
        }

        AddDailySummarySheet(workbook, data.DailySummaries);
        workbook.SaveAs(path);
    }

    private static void AddFocusRecordsSheet(
        XLWorkbook workbook,
        IReadOnlyList<FocusExportRecord> records)
    {
        var worksheet = workbook.Worksheets.Add("专注记录");
        WriteHeaders(worksheet, ["记录ID", "日期", "开始时间", "结束时间", "专注时长(分钟)", "目标"]);

        for (var index = 0; index < records.Count; index++)
        {
            var row = index + 2;
            var record = records[index];
            worksheet.Cell(row, 1).Value = record.SessionId.ToString();
            SetDate(worksheet.Cell(row, 2), record.Date);
            SetTime(worksheet.Cell(row, 3), record.StartTime);
            SetTime(worksheet.Cell(row, 4), record.EndTime);
            worksheet.Cell(row, 5).Value = record.DurationMinutes;
            worksheet.Cell(row, 6).Value = record.GoalName ?? string.Empty;
        }

        FinishSheet(worksheet, records.Count + 1, 6, [22d, 13d, 11d, 11d, 18d, 36d]);
    }

    private static void AddTaskDetailsSheet(
        XLWorkbook workbook,
        IReadOnlyList<FocusTaskExportRecord> records)
    {
        var worksheet = workbook.Worksheets.Add("任务明细");
        WriteHeaders(worksheet, ["专注记录ID", "日期", "目标", "完成任务"]);

        for (var index = 0; index < records.Count; index++)
        {
            var row = index + 2;
            var record = records[index];
            worksheet.Cell(row, 1).Value = record.SessionId.ToString();
            SetDate(worksheet.Cell(row, 2), record.Date);
            worksheet.Cell(row, 3).Value = record.GoalName ?? string.Empty;
            worksheet.Cell(row, 4).Value = record.TaskName;
        }

        FinishSheet(worksheet, records.Count + 1, 4, [22d, 13d, 36d, 48d]);
    }

    private static void AddDailySummarySheet(
        XLWorkbook workbook,
        IReadOnlyList<FocusDailyExportSummary> summaries)
    {
        var worksheet = workbook.Worksheets.Add("每日汇总");
        WriteHeaders(worksheet, ["日期", "总专注时长(分钟)", "专注次数", "完成任务数"]);

        for (var index = 0; index < summaries.Count; index++)
        {
            var row = index + 2;
            var summary = summaries[index];
            SetDate(worksheet.Cell(row, 1), summary.Date);
            worksheet.Cell(row, 2).Value = summary.DurationMinutes;
            worksheet.Cell(row, 3).Value = summary.FocusCount;
            worksheet.Cell(row, 4).Value = summary.CompletedTaskCount;
        }

        FinishSheet(worksheet, summaries.Count + 1, 4, [13d, 20d, 13d, 15d]);
    }

    private static void WriteHeaders(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var header = worksheet.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderBackground);
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void SetDate(IXLCell cell, DateOnly date)
    {
        cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "yyyy-mm-dd";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void SetTime(IXLCell cell, DateTime time)
    {
        cell.Value = time.TimeOfDay;
        cell.Style.DateFormat.Format = "hh:mm";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void FinishSheet(
        IXLWorksheet worksheet,
        int lastRow,
        int lastColumn,
        IReadOnlyList<double> maximumWidths)
    {
        worksheet.Style.Font.FontName = "Microsoft YaHei UI";
        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, Math.Max(1, lastRow), lastColumn).SetAutoFilter();
        worksheet.Columns(1, lastColumn).AdjustToContents();
        for (var column = 1; column <= lastColumn; column++)
        {
            worksheet.Column(column).Width = Math.Min(
                Math.Max(worksheet.Column(column).Width, 10d),
                maximumWidths[column - 1]);
        }

        worksheet.Range(1, 1, Math.Max(1, lastRow), lastColumn)
            .Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }
}
