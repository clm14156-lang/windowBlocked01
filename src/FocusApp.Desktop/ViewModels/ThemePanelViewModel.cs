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
    private string? _originalThemeKey;
    private string? _previewThemeKey;
    private bool _isLoggedIn;
    private bool _isVip;

    public ThemePanelViewModel()
    {
        CloseCommand = new RelayCommand<object>(_ => Close());
        SelectThemeCommand = new RelayCommand<string>(SelectTheme);
        PreviewThemeCommand = new RelayCommand<string>(PreviewTheme);
        RestoreOriginalThemeCommand = new RelayCommand<object>(_ => RestoreOriginalTheme());
        OpenVipCommand = new RelayCommand<object>(_ => RequestVip());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? ThemeSelected;

    public event EventHandler? VipRequested;

    public ICommand CloseCommand { get; }

    public ICommand SelectThemeCommand { get; }

    public ICommand PreviewThemeCommand { get; }

    public ICommand RestoreOriginalThemeCommand { get; }

    public ICommand OpenVipCommand { get; }

    public bool CanUsePremiumThemes => _isLoggedIn && _isVip;

    public bool IsThemePreviewing => !string.IsNullOrWhiteSpace(PreviewThemeKey);

    public string? VisualSelectedThemeKey => IsThemePreviewing ? null : SelectedThemeKey;

    public string? OriginalThemeKey
    {
        get => _originalThemeKey;
        private set
        {
            if (_originalThemeKey == value)
            {
                return;
            }

            _originalThemeKey = value;
            OnPropertyChanged();
        }
    }

    public string? PreviewThemeKey
    {
        get => _previewThemeKey;
        private set
        {
            if (_previewThemeKey == value)
            {
                return;
            }

            _previewThemeKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsThemePreviewing));
            OnPropertyChanged(nameof(VisualSelectedThemeKey));
            OnPropertyChanged(nameof(PreviewStatusDisplay));
        }
    }

    public string PreviewStatusDisplay => IsThemePreviewing
        ? $"正在预览：{GetThemeDisplayName(PreviewThemeKey!)}主题"
        : string.Empty;

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
            OnPropertyChanged(nameof(VisualSelectedThemeKey));
        }
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            IsOpen = true;
        }
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
        if (CanUsePremiumThemes && IsThemePreviewing)
        {
            var previewThemeKey = PreviewThemeKey!;
            ClearPreviewState();
            SelectedThemeKey = previewThemeKey;
            ThemeSelected?.Invoke(this, previewThemeKey);
        }
        else if (!CanUsePremiumThemes)
        {
            RestoreOriginalTheme();
            if (IsPremiumTheme(SelectedThemeKey))
            {
                SelectedThemeKey = "Orange";
                ThemeSelected?.Invoke(this, SelectedThemeKey);
            }
        }
    }

    private void Close()
    {
        RestoreOriginalTheme();
        IsOpen = false;
    }

    private void SelectTheme(string? themeKey)
    {
        if (!string.IsNullOrWhiteSpace(themeKey) &&
            (CanUsePremiumThemes || !IsPremiumTheme(themeKey)))
        {
            ClearPreviewState();
            SelectedThemeKey = themeKey;
            ThemeSelected?.Invoke(this, themeKey);
        }
    }

    private void PreviewTheme(string? themeKey)
    {
        if (CanUsePremiumThemes ||
            string.IsNullOrWhiteSpace(themeKey) ||
            !IsPremiumTheme(themeKey))
        {
            return;
        }

        OriginalThemeKey ??= SelectedThemeKey;
        PreviewThemeKey = themeKey;
        ThemeSelected?.Invoke(this, themeKey);
    }

    private void RestoreOriginalTheme()
    {
        if (!IsThemePreviewing || string.IsNullOrWhiteSpace(OriginalThemeKey))
        {
            ClearPreviewState();
            return;
        }

        var originalThemeKey = OriginalThemeKey;
        ClearPreviewState();
        ThemeSelected?.Invoke(this, originalThemeKey);
    }

    private void ClearPreviewState()
    {
        PreviewThemeKey = null;
        OriginalThemeKey = null;
    }

    private void RequestVip()
    {
        VipRequested?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsPremiumTheme(string themeKey) => PremiumThemeKeys.Contains(themeKey);

    private static string GetThemeDisplayName(string themeKey) => themeKey switch
    {
        "Warm" => "暖阳",
        "Sky" => "天空",
        "Dream" => "梦幻",
        "Fresh" => "清晰",
        "Starry" => "星空",
        "Mountain" => "山脉",
        "Forest" => "森林",
        "Snow" => "雪山",
        "Blue" => "蓝色",
        "Cyan" => "青色",
        "Dark" => "暗黑",
        _ => "橙色"
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
