using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public FocusTargetViewModel(string name, IEnumerable<string>? taskNames = null)
    {
        TargetId = Guid.NewGuid().ToString("N");
        Name = name;
        Tasks = new TargetTaskCollection(TargetId);
        foreach (var taskName in taskNames ?? [])
        {
            AddTask(taskName);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string TargetId { get; }

    public ObservableCollection<FocusTaskViewModel> Tasks { get; }

    public FocusTaskViewModel AddTask(string name, bool isNew = false, bool insertAtTop = false)
    {
        var task = new FocusTaskViewModel(TargetId, name, isNew);
        if (insertAtTop)
        {
            Tasks.Insert(0, task);
        }
        else
        {
            Tasks.Add(task);
        }

        return task;
    }

    public bool RemoveTask(FocusTaskViewModel task) =>
        task.TargetId == TargetId && Tasks.Remove(task);

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

    private sealed class TargetTaskCollection(string targetId) : ObservableCollection<FocusTaskViewModel>
    {
        protected override void InsertItem(int index, FocusTaskViewModel item)
        {
            EnsureBelongsToTarget(item);
            base.InsertItem(index, item);
        }

        protected override void SetItem(int index, FocusTaskViewModel item)
        {
            EnsureBelongsToTarget(item);
            base.SetItem(index, item);
        }

        private void EnsureBelongsToTarget(FocusTaskViewModel task)
        {
            if (task.TargetId != targetId)
            {
                throw new InvalidOperationException("任务必须属于当前目标。");
            }
        }
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
    private bool _isDragging;
    private bool _showDropBefore;
    private bool _showDropAfter;

    internal FocusTaskViewModel(string targetId, string name, bool isNew = false)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("任务必须绑定目标。", nameof(targetId));
        }

        TargetId = targetId;
        _name = name;
        _editName = name;
        _isNew = isNew;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TargetId { get; }

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

    public bool IsDragging
    {
        get => _isDragging;
        internal set
        {
            if (_isDragging == value)
            {
                return;
            }

            _isDragging = value;
            OnPropertyChanged();
        }
    }

    public bool ShowDropBefore
    {
        get => _showDropBefore;
        internal set
        {
            if (_showDropBefore == value)
            {
                return;
            }

            _showDropBefore = value;
            OnPropertyChanged();
        }
    }

    public bool ShowDropAfter
    {
        get => _showDropAfter;
        internal set
        {
            if (_showDropAfter == value)
            {
                return;
            }

            _showDropAfter = value;
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
            CancelEdit();
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
