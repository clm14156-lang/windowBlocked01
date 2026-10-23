using System.ComponentModel;
using System.Windows.Input;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusCharacterPickerViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _selectedId = FocusCharacters.DefaultId;
    private bool _hasVipAccess;

    public FocusCharacterPickerViewModel()
    {
        var options = FocusCharacters.All.Select(role => new FocusCharacterOptionViewModel(role)).ToArray();
        Rows = options.Chunk(3).Select(row => (IReadOnlyList<FocusCharacterOptionViewModel>)Array.AsReadOnly(row)).ToArray();
        options[0].IsSelected = true;
        OpenCommand = new RelayCommand<object>(_ => SetOpen(true));
        CloseCommand = new RelayCommand<object>(_ => SetOpen(false));
        SelectCommand = new RelayCommand<FocusCharacterOptionViewModel>(option =>
        {
            if (option is null || !ApplySelection(option.Id)) return;
            SelectionChanged?.Invoke(this, SelectedId);
        }, option => option is not null && (FocusCharacters.Find(option.Id).IsFree || _hasVipAccess));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? SelectionChanged;
    public IReadOnlyList<IReadOnlyList<FocusCharacterOptionViewModel>> Rows { get; }
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand SelectCommand { get; }
    public bool IsOpen => _isOpen;
    public string SelectedId => _selectedId;
    public string SelectedName => FocusCharacters.Find(SelectedId).Name;

    public bool ApplySelection(string? id)
    {
        var character = FocusCharacters.Find(id);
        if (!character.IsFree && !_hasVipAccess) return false;
        id = character.Id;
        if (_selectedId == id) return false;
        _selectedId = id;
        foreach (var option in Rows.SelectMany(row => row)) option.IsSelected = option.Id == id;
        PropertyChanged?.Invoke(this, new(nameof(SelectedId)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedName)));
        return true;
    }

    public void SetUserAccess(bool isLoggedIn, bool isVip)
    {
        var hasVipAccess = isLoggedIn && isVip;
        if (_hasVipAccess == hasVipAccess) return;
        _hasVipAccess = hasVipAccess;
        foreach (var option in Rows.SelectMany(row => row)) option.SetVipAccess(hasVipAccess);
        if (!hasVipAccess && !FocusCharacters.Find(SelectedId).IsFree && ApplySelection(FocusCharacters.DefaultId))
            SelectionChanged?.Invoke(this, SelectedId);
        ((RelayCommand<FocusCharacterOptionViewModel>)SelectCommand).NotifyCanExecuteChanged();
    }

    private void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;
        PropertyChanged?.Invoke(this, new(nameof(IsOpen)));
    }
}

public sealed class FocusCharacterOptionViewModel(FocusCharacter character) : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isLocked = !character.IsFree;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => character.Id;
    public string Name => character.Name;
    public bool IsLocked => _isLocked;
    public bool CanSelect => !IsLocked;
    internal void SetVipAccess(bool hasVipAccess)
    {
        var locked = !character.IsFree && !hasVipAccess;
        if (_isLocked == locked) return;
        _isLocked = locked;
        PropertyChanged?.Invoke(this, new(nameof(IsLocked)));
        PropertyChanged?.Invoke(this, new(nameof(CanSelect)));
    }
    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        }
    }
}
