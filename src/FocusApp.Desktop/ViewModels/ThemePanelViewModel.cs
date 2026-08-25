using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class ThemePanelViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> PremiumThemeKeys =
    [
        "Warm",
        "Sky",
        "Dream",
        "Fresh",
        "Starry",
        "Mountain",
        "Forest",
        "Snow"
    ];

    private bool _isOpen;
    private string _selectedThemeKey = "Orange";
    private bool _isLoggedIn;
    private bool _isVip;

    public ThemePanelViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SelectThemeCommand = new RelayCommand<string>(SelectTheme);
        OpenVipCommand = new RelayCommand<object>(_ => RequestVip());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? ThemeSelected;

    public event EventHandler? VipRequested;

    public ICommand CloseCommand { get; }

    public ICommand SelectThemeCommand { get; }

    public ICommand OpenVipCommand { get; }

    public bool CanUsePremiumThemes => _isLoggedIn && _isVip;

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

    public void SetUserAccess(bool isLoggedIn, bool isVip)
    {
        var canUsePremiumThemes = CanUsePremiumThemes;
        _isLoggedIn = isLoggedIn;
        _isVip = isVip;
        if (canUsePremiumThemes == CanUsePremiumThemes)
        {
            return;
        }

        OnPropertyChanged(nameof(CanUsePremiumThemes));
        if (!CanUsePremiumThemes && IsPremiumTheme(SelectedThemeKey))
        {
            SelectedThemeKey = "Orange";
            ThemeSelected?.Invoke(this, SelectedThemeKey);
        }
    }

    private void Close()
    {
        IsOpen = false;
    }

    private void SelectTheme(string? themeKey)
    {
        if (!string.IsNullOrWhiteSpace(themeKey) &&
            (CanUsePremiumThemes || !IsPremiumTheme(themeKey)))
        {
            SelectedThemeKey = themeKey;
            ThemeSelected?.Invoke(this, themeKey);
        }
    }

    private void RequestVip()
    {
        Close();
        VipRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsPremiumTheme(string themeKey) => PremiumThemeKeys.Contains(themeKey);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
