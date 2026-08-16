using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FocusApp.Desktop.ViewModels;

public sealed class SettingsPageViewModel : INotifyPropertyChanged
{
    private SettingsEntryItemViewModel? _activeEntry;

    public SettingsPageViewModel(
        IEnumerable<SettingsToggleItemViewModel> toggleItems,
        IEnumerable<SettingsEntryItemViewModel> entryItems)
    {
        ToggleItems = new ReadOnlyCollection<SettingsToggleItemViewModel>(toggleItems.ToList());
        EntryItems = new ReadOnlyCollection<SettingsEntryItemViewModel>(entryItems.ToList());
        ActivateEntryCommand = new RelayCommand<SettingsEntryItemViewModel>(ActivateEntry);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyCollection<SettingsToggleItemViewModel> ToggleItems { get; }

    public ReadOnlyCollection<SettingsEntryItemViewModel> EntryItems { get; }

    public ICommand ActivateEntryCommand { get; }

    public string? LastActivatedEntryKey => _activeEntry?.Key;

    private void ActivateEntry(SettingsEntryItemViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (_activeEntry is not null)
        {
            _activeEntry.IsActive = false;
        }

        _activeEntry = entry;
        _activeEntry.IsActive = true;
        OnPropertyChanged(nameof(LastActivatedEntryKey));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsToggleItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled;

    public SettingsToggleItemViewModel(
        string key,
        string title,
        string description,
        string icon,
        bool isEnabled,
        bool hasSeparator = true)
    {
        Key = key;
        Title = title;
        Description = description;
        Icon = icon;
        _isEnabled = isEnabled;
        HasSeparator = hasSeparator;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string Icon { get; }

    public bool HasSeparator { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
}

public sealed class SettingsEntryItemViewModel : INotifyPropertyChanged
{
    private bool _isActive;

    public SettingsEntryItemViewModel(
        string key,
        string title,
        string description,
        string icon,
        bool hasSeparator = true)
    {
        Key = key;
        Title = title;
        Description = description;
        Icon = icon;
        HasSeparator = hasSeparator;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string Title { get; }

    public string Description { get; }

    public string Icon { get; }

    public bool HasSeparator { get; }

    public bool IsActive
    {
        get => _isActive;
        internal set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}
