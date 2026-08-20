using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public FocusTargetViewModel(string name, IEnumerable<FocusTaskViewModel>? tasks = null)
    {
        Name = name;
        Tasks = new ObservableCollection<FocusTaskViewModel>(tasks ?? []);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public ObservableCollection<FocusTaskViewModel> Tasks { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class FocusTaskViewModel : INotifyPropertyChanged
{
    private string _name;
    private string _editName;
    private bool _isMenuOpen;
    private bool _isHovered;
    private bool _isFocusMenuOpen;
    private bool _isEditing;
    private bool _isCompleted;
    private bool _isNew;

    public FocusTaskViewModel(string name, bool isNew = false)
    {
        _name = name;
        _editName = name;
        _isNew = isNew;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        private set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public string EditName
    {
        get => _editName;
        set
        {
            if (_editName == value)
            {
                return;
            }

            _editName = value;
            OnPropertyChanged();
        }
    }

    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        set
        {
            if (_isMenuOpen == value)
            {
                return;
            }

            _isMenuOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMenuButtonVisible));
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered == value)
            {
                return;
            }

            _isHovered = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMenuButtonVisible));
        }
    }

    public bool IsMenuButtonVisible => IsHovered || IsMenuOpen;

    public bool IsFocusMenuOpen
    {
        get => _isFocusMenuOpen;
        set
        {
            if (_isFocusMenuOpen == value)
            {
                return;
            }

            _isFocusMenuOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value)
            {
                return;
            }

            _isCompleted = value;
            OnPropertyChanged();
        }
    }

    public bool IsNew
    {
        get => _isNew;
        private set
        {
            if (_isNew == value)
            {
                return;
            }

            _isNew = value;
            OnPropertyChanged();
        }
    }

    public void BeginEdit()
    {
        EditName = Name;
        IsMenuOpen = false;
        IsFocusMenuOpen = false;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        var name = EditName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        Name = name;
        IsEditing = false;
        IsNew = false;
    }

    public void CancelEdit()
    {
        EditName = Name;
        IsEditing = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
