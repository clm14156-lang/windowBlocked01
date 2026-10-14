using System.ComponentModel;
using System.Windows.Input;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusCharacterPickerViewModel : INotifyPropertyChanged
{
    private bool _isOpen;
    private string _selectedId = FocusCharacters.DefaultId;

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
        });
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
        id = FocusCharacters.Find(id).Id;
        if (_selectedId == id) return false;
        _selectedId = id;
        foreach (var option in Rows.SelectMany(row => row)) option.IsSelected = option.Id == id;
        PropertyChanged?.Invoke(this, new(nameof(SelectedId)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedName)));
        return true;
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
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => character.Id;
    public string Name => character.Name;
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
