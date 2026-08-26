using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public enum ExportRecordsPopup
{
    None,
    TimeRange,
    FileFormat,
    IncludedContent
}

public sealed class ExportRecordsModalViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private ExportRecordsPopup _activePopup;
    private string _selectedTimeRange = "本月";
    private string _selectedFileFormat = "Excel (.xlsx)";
    private bool _includeFocusRecords = true;
    private bool _includeTaskDetails = true;

    public ExportRecordsModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        CancelCommand = new RelayCommand<object>(_ => Close());
        ExportCommand = new RelayCommand<object>(_ => ActivePopup = ExportRecordsPopup.None);
        ToggleTimeRangePopupCommand = new RelayCommand<object>(_ => TogglePopup(ExportRecordsPopup.TimeRange));
        ToggleFileFormatPopupCommand = new RelayCommand<object>(_ => TogglePopup(ExportRecordsPopup.FileFormat));
        ToggleIncludedContentPopupCommand = new RelayCommand<object>(_ => TogglePopup(ExportRecordsPopup.IncludedContent));
        CompleteIncludedContentCommand = new RelayCommand<object>(_ => ActivePopup = ExportRecordsPopup.None);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ToggleTimeRangePopupCommand { get; }
    public ICommand ToggleFileFormatPopupCommand { get; }
    public ICommand ToggleIncludedContentPopupCommand { get; }
    public ICommand CompleteIncludedContentCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
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
            OnPropertyChanged(nameof(IsFileFormatPopupOpen));
            OnPropertyChanged(nameof(IsIncludedContentPopupOpen));
        }
    }

    public bool IsTimeRangePopupOpen
    {
        get => ActivePopup == ExportRecordsPopup.TimeRange;
        set => SetPopupOpen(ExportRecordsPopup.TimeRange, value);
    }

    public bool IsFileFormatPopupOpen
    {
        get => ActivePopup == ExportRecordsPopup.FileFormat;
        set => SetPopupOpen(ExportRecordsPopup.FileFormat, value);
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

    public string SelectedFileFormat
    {
        get => _selectedFileFormat;
        private set
        {
            if (SetField(ref _selectedFileFormat, value))
            {
                OnPropertyChanged(nameof(IsExcelSelected));
                OnPropertyChanged(nameof(IsCsvSelected));
            }
        }
    }

    public bool IsExcelSelected
    {
        get => SelectedFileFormat == "Excel (.xlsx)";
        set
        {
            if (value)
            {
                SelectedFileFormat = "Excel (.xlsx)";
                ActivePopup = ExportRecordsPopup.None;
            }
        }
    }

    public bool IsCsvSelected
    {
        get => SelectedFileFormat == "CSV (.csv)";
        set
        {
            if (value)
            {
                SelectedFileFormat = "CSV (.csv)";
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
        SelectedTimeRange = "本月";
        SelectedFileFormat = "Excel (.xlsx)";
        IncludeFocusRecords = true;
        IncludeTaskDetails = true;
        ActivePopup = ExportRecordsPopup.None;
        IsOpen = true;
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
