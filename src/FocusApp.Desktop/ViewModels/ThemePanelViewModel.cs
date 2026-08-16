using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class ThemePanelViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _selectedThemeKey = "Warm";

    public ThemePanelViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SelectThemeCommand = new RelayCommand<string>(SelectTheme);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? ThemeSelected;

    public ICommand CloseCommand { get; }

    public ICommand SelectThemeCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen == value)
            {
                return;
            }

            _isOpen = value;
            OnPropertyChanged();
        }
    }

    public string SelectedThemeKey
    {
        get => _selectedThemeKey;
        private set
        {
            if (_selectedThemeKey == value)
            {
                return;
            }

            _selectedThemeKey = value;
            OnPropertyChanged();
        }
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void SelectTheme(string? themeKey)
    {
        if (!string.IsNullOrWhiteSpace(themeKey))
        {
            SelectedThemeKey = themeKey;
            ThemeSelected?.Invoke(this, themeKey);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
