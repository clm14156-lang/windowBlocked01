using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class AccountSyncModalViewModel : INotifyPropertyChanged
{
    public const string CloudSection = "Cloud";
    public const string DevicesSection = "Devices";
    public const string SecuritySection = "Security";

    private bool _isOpen;
    private string _selectedSectionKey = CloudSection;

    public AccountSyncModalViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        NavigateSectionCommand = new RelayCommand<string>(SelectSection);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CloseCommand { get; }

    public ICommand NavigateSectionCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public string SelectedSectionKey
    {
        get => _selectedSectionKey;
        private set => SetField(ref _selectedSectionKey, value);
    }

    public void Open()
    {
        SelectedSectionKey = CloudSection;
        IsOpen = true;
    }

    public void SelectSection(string? sectionKey)
    {
        if (sectionKey is CloudSection or DevicesSection or SecuritySection)
        {
            SelectedSectionKey = sectionKey;
        }
    }

    private void Close()
    {
        IsOpen = false;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
