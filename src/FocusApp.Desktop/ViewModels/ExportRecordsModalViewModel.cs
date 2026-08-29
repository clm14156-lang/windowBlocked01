using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public enum ExportRecordsPopup
{
    None,
    TimeRange,
    IncludedContent
}

public enum ExportNotificationKind
{
    Information,
    Success,
    Failure
}

public sealed class ExportRecordsModalViewModel : INotifyPropertyChanged
{
    private readonly IFocusRecordsExporter? _exporter;
    private readonly RelayCommand<object> _exportCommand;
    private bool _isOpen;
    private bool _isExporting;
    private ExportRecordsPopup _activePopup;
    private string _selectedTimeRange = "本月";
    private bool _includeFocusRecords = true;
    private bool _includeTaskDetails = true;
    private DispatcherTimer? _notificationTimer;
    private bool _isNotificationVisible;
    private string _notificationTitle = string.Empty;
    private string _notificationMessage = string.Empty;
    private ExportNotificationKind _notificationKind;

    public ExportRecordsModalViewModel(IFocusRecordsExporter? exporter = null)
    {
        _exporter = exporter;
        CloseCommand = new RelayCommand<object>(_ => Close());
        CancelCommand = new RelayCommand<object>(_ => Close());
        _exportCommand = new RelayCommand<object>(_ => Export(), _ => CanExport);
        ExportCommand = _exportCommand;
        ToggleTimeRangePopupCommand = new RelayCommand<object>(_ => TogglePopup(ExportRecordsPopup.TimeRange));
        ToggleIncludedContentPopupCommand = new RelayCommand<object>(_ => TogglePopup(ExportRecordsPopup.IncludedContent));
        CompleteIncludedContentCommand = new RelayCommand<object>(_ => ActivePopup = ExportRecordsPopup.None);
        CloseNotificationCommand = new RelayCommand<object>(_ => CloseNotification());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ToggleTimeRangePopupCommand { get; }
    public ICommand ToggleIncludedContentPopupCommand { get; }
    public ICommand CompleteIncludedContentCommand { get; }
    public ICommand CloseNotificationCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetField(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                _exportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanExport =>
        !IsExporting &&
        (IncludeFocusRecords || IncludeTaskDetails);

    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        private set => SetField(ref _isNotificationVisible, value);
    }

    public string NotificationTitle
    {
        get => _notificationTitle;
        private set => SetField(ref _notificationTitle, value);
    }

    public string NotificationMessage
    {
        get => _notificationMessage;
        private set => SetField(ref _notificationMessage, value);
    }

    public ExportNotificationKind NotificationKind
    {
        get => _notificationKind;
        private set => SetField(ref _notificationKind, value);
    }

    public ExportRecordsPopup ActivePopup
    {
        get => _activePopup;
        private set
        {
            if (!SetField(ref _activePopup, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTimeRangePopupOpen));
            OnPropertyChanged(nameof(IsIncludedContentPopupOpen));
        }
    }

    public bool IsTimeRangePopupOpen
    {
        get => ActivePopup == ExportRecordsPopup.TimeRange;
        set => SetPopupOpen(ExportRecordsPopup.TimeRange, value);
    }

    public bool IsIncludedContentPopupOpen
    {
        get => ActivePopup == ExportRecordsPopup.IncludedContent;
        set => SetPopupOpen(ExportRecordsPopup.IncludedContent, value);
    }

    public string SelectedTimeRange
    {
        get => _selectedTimeRange;
        private set
        {
            if (SetField(ref _selectedTimeRange, value))
            {
                OnPropertyChanged(nameof(IsCurrentMonthSelected));
                OnPropertyChanged(nameof(IsAllRecordsSelected));
            }
        }
    }

    public bool IsCurrentMonthSelected
    {
        get => SelectedTimeRange == "本月";
        set
        {
            if (value)
            {
                SelectedTimeRange = "本月";
                ActivePopup = ExportRecordsPopup.None;
            }
        }
    }

    public bool IsAllRecordsSelected
    {
        get => SelectedTimeRange == "全部记录";
        set
        {
            if (value)
            {
                SelectedTimeRange = "全部记录";
                ActivePopup = ExportRecordsPopup.None;
            }
        }
    }

    public bool IncludeFocusRecords
    {
        get => _includeFocusRecords;
        set
        {
            if (SetField(ref _includeFocusRecords, value))
            {
                OnPropertyChanged(nameof(IncludedContentSummary));
                OnPropertyChanged(nameof(CanExport));
                _exportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IncludeTaskDetails
    {
        get => _includeTaskDetails;
        set
        {
            if (SetField(ref _includeTaskDetails, value))
            {
                OnPropertyChanged(nameof(IncludedContentSummary));
                OnPropertyChanged(nameof(CanExport));
                _exportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string IncludedContentSummary => (IncludeFocusRecords, IncludeTaskDetails) switch
    {
        (true, true) => "专注记录、任务明细",
        (true, false) => "专注记录",
        (false, true) => "任务明细",
        _ => "未选择"
    };

    public void Open()
    {
        CloseNotification();
        SelectedTimeRange = "本月";
        IncludeFocusRecords = true;
        IncludeTaskDetails = true;
        ActivePopup = ExportRecordsPopup.None;
        IsOpen = true;
    }

    private async void Export()
    {
        ActivePopup = ExportRecordsPopup.None;
        if (_exporter is null || !CanExport)
        {
            return;
        }

        IsExporting = true;
        FocusExportResult result;
        try
        {
            result = await _exporter.ExportAsync(new FocusExportOptions(
                IsCurrentMonthSelected
                    ? FocusExportTimeRange.CurrentMonth
                    : FocusExportTimeRange.AllRecords,
                IncludeFocusRecords,
                IncludeTaskDetails));
        }
        catch (Exception)
        {
            result = new FocusExportResult(
                FocusExportResultKind.Failure,
                "无法导出专注记录，请稍后重试");
        }
        finally
        {
            IsExporting = false;
        }

        switch (result.Kind)
        {
            case FocusExportResultKind.Success:
                Close();
                ShowNotification(
                    ExportNotificationKind.Success,
                    "导出成功",
                    "专注记录已保存");
                break;
            case FocusExportResultKind.NoData:
                ShowNotification(
                    ExportNotificationKind.Information,
                    "暂无可导出记录",
                    "所选时间范围内没有专注记录");
                break;
            case FocusExportResultKind.Failure:
                ShowNotification(
                    ExportNotificationKind.Failure,
                    "导出失败",
                    result.ErrorMessage ?? "无法导出专注记录，请稍后重试");
                break;
        }
    }

    private void ShowNotification(
        ExportNotificationKind kind,
        string title,
        string message)
    {
        NotificationKind = kind;
        NotificationTitle = title;
        NotificationMessage = message;
        IsNotificationVisible = true;

        _notificationTimer ??= new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _notificationTimer.Stop();
        _notificationTimer.Tick -= NotificationTimer_Tick;
        _notificationTimer.Tick += NotificationTimer_Tick;
        _notificationTimer.Start();
    }

    private void NotificationTimer_Tick(object? sender, EventArgs e) => CloseNotification();

    private void CloseNotification()
    {
        _notificationTimer?.Stop();
        IsNotificationVisible = false;
    }

    private void Close()
    {
        ActivePopup = ExportRecordsPopup.None;
        IsOpen = false;
    }

    private void TogglePopup(ExportRecordsPopup popup) =>
        ActivePopup = ActivePopup == popup ? ExportRecordsPopup.None : popup;

    private void SetPopupOpen(ExportRecordsPopup popup, bool isOpen)
    {
        if (isOpen)
        {
            ActivePopup = popup;
        }
        else if (ActivePopup == popup)
        {
            ActivePopup = ExportRecordsPopup.None;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
