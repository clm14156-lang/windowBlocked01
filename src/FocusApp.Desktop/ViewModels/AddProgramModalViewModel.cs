using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class AddProgramModalViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "backgroundtaskhost.exe", "conhost.exe", "csrss.exe", "ctfmon.exe", "dllhost.exe",
        "dwm.exe", "fontdrvhost.exe", "lsass.exe", "registry.exe", "runtimebroker.exe",
        "services.exe", "sihost.exe", "smss.exe", "svchost.exe", "system.exe",
        "taskhostw.exe", "wininit.exe", "winlogon.exe"
    };

    private readonly List<RecentProgramRecordViewModel> _recentPrograms = [];
    private bool _isOpen;
    private string _searchText = string.Empty;
    private bool _wasChooseProgramPressed;
    private RecentProgramRecordViewModel? _selectedProgram;

    public AddProgramModalViewModel(IEnumerable<RecentProgramRecord>? recentPrograms = null)
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SaveCommand = new RelayCommand<object>(_ => Save());
        ChooseProgramCommand = new RelayCommand<object>(_ => RequestProgramSelection());
        SelectProgramCommand = new RelayCommand<RecentProgramRecordViewModel>(program => SelectedProgram = program);

        foreach (var program in recentPrograms ?? []) RecordProgramRun(program);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<RecentProgramRecordViewModel>? ProgramSelected;
    public event EventHandler? ChooseProgramRequested;
    public ICommand CloseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ChooseProgramCommand { get; }
    public ICommand SelectProgramCommand { get; }

    public IEnumerable<RecentProgramRecordViewModel> RecentPrograms => _recentPrograms
        .Where(MatchesSearch).OrderByDescending(program => program.LastRunTime);
    public bool HasRecentPrograms => _recentPrograms.Count > 0;
    public bool HasVisibleRecentPrograms => RecentPrograms.Any();

    public bool IsOpen
    {
        get => _isOpen;
        private set { if (_isOpen == value) return; _isOpen = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecentPrograms));
            OnPropertyChanged(nameof(HasVisibleRecentPrograms));
        }
    }

    public RecentProgramRecordViewModel? SelectedProgram
    {
        get => _selectedProgram;
        set { if (ReferenceEquals(_selectedProgram, value)) return; _selectedProgram = value; OnPropertyChanged(); }
    }

    public bool WasChooseProgramPressed
    {
        get => _wasChooseProgramPressed;
        private set { if (_wasChooseProgramPressed == value) return; _wasChooseProgramPressed = value; OnPropertyChanged(); }
    }

    public void Open()
    {
        SearchText = string.Empty;
        SelectedProgram = null;
        WasChooseProgramPressed = false;
        IsOpen = true;
    }

    public void RecordProgramRun(RecentProgramRecord program)
    {
        if (string.IsNullOrWhiteSpace(program.ExePath) || IsIgnoredProcess(program.ProcessName)) return;
        UpsertProgram(program);
    }

    private void UpsertProgram(RecentProgramRecord program)
    {
        var existing = _recentPrograms.FirstOrDefault(item => string.Equals(item.ExePath, program.ExePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null) _recentPrograms.Add(new RecentProgramRecordViewModel(program));
        else if (program.LastRunTime > existing.LastRunTime) existing.UpdateLastRunTime(program.LastRunTime);
        OnPropertyChanged(nameof(RecentPrograms));
        OnPropertyChanged(nameof(HasRecentPrograms));
        OnPropertyChanged(nameof(HasVisibleRecentPrograms));
    }

    public void ApplyProgramSelection(ProgramFileSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var path = Path.GetFullPath(selection.ExePath);
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SearchText = string.Empty;
        UpsertProgram(new RecentProgramRecord(
            path,
            selection.ProcessName,
            selection.DisplayName,
            DateTime.Now));
        SelectedProgram = _recentPrograms.FirstOrDefault(program =>
            string.Equals(program.ExePath, path, StringComparison.OrdinalIgnoreCase));
    }

    private void RequestProgramSelection()
    {
        WasChooseProgramPressed = true;
        ChooseProgramRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsIgnoredProcess(string processName)
    {
        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : $"{processName}.exe";
        return IgnoredProcessNames.Contains(normalized);
    }

    private bool MatchesSearch(RecentProgramRecordViewModel program)
    {
        var search = SearchText.Trim();
        return search.Length == 0 || program.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               program.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) || program.ExePath.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void Save()
    {
        if (SelectedProgram is not null) ProgramSelected?.Invoke(this, SelectedProgram);
        Close();
    }

    private void Close() => IsOpen = false;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record RecentProgramRecord(string ExePath, string ProcessName, string DisplayName, DateTime LastRunTime);

public sealed class RecentProgramRecordViewModel : INotifyPropertyChanged
{
    private DateTime _lastRunTime;
    private ImageSource? _icon;
    public RecentProgramRecordViewModel(RecentProgramRecord record) { ExePath = record.ExePath; ProcessName = record.ProcessName; DisplayName = record.DisplayName; _lastRunTime = record.LastRunTime; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string ExePath { get; }
    public string ProcessName { get; }
    public string DisplayName { get; }
    public DateTime LastRunTime => _lastRunTime;
    public string LastRunTimeDisplay => LastRunTime.Date == DateTime.Today ? LastRunTime.ToString("HH:mm") : LastRunTime.ToString("M月d日");
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }
    public void UpdateLastRunTime(DateTime lastRunTime)
    {
        if (_lastRunTime == lastRunTime) return;
        _lastRunTime = lastRunTime;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastRunTime)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastRunTimeDisplay)));
    }
}
