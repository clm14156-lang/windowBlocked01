using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FocusApp.Desktop.Services;

public sealed class FocusRecordsExporter(
    DesktopServiceConnection serviceConnection,
    FocusExportDataService dataService,
    ExcelExportService excelService,
    Func<DateTime>? clock = null) : IFocusRecordsExporter
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);

    public async Task<FocusExportResult> ExportAsync(
        FocusExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var now = _clock();
            var snapshot = await serviceConnection.RefreshStateAsync(cancellationToken);
            var data = dataService.Build(snapshot, options.TimeRange, now);
            if (data.FocusRecords.Count == 0)
            {
                return new FocusExportResult(FocusExportResultKind.NoData);
            }

            var saveDialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".xlsx",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = options.TimeRange == FocusExportTimeRange.CurrentMonth
                    ? $"专注记录_{now:yyyy-MM}.xlsx"
                    : "专注记录_全部记录.xlsx",
                OverwritePrompt = true,
                Title = "导出专注记录"
            };
            if (saveDialog.ShowDialog() != true)
            {
                return new FocusExportResult(FocusExportResultKind.Cancelled);
            }

            await Task.Run(
                () => excelService.Save(saveDialog.FileName, data, options),
                cancellationToken);
            return new FocusExportResult(FocusExportResultKind.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new FocusExportResult(FocusExportResultKind.Cancelled);
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.WriteLine(exception);
            return new FocusExportResult(
                FocusExportResultKind.Failure,
                "没有权限写入所选位置，请选择其他位置");
        }
        catch (IOException exception)
        {
            Debug.WriteLine(exception);
            return new FocusExportResult(
                FocusExportResultKind.Failure,
                "文件正在被其他程序使用，请关闭后重试");
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            return new FocusExportResult(
                FocusExportResultKind.Failure,
                "无法导出专注记录，请稍后重试");
        }
    }
}
