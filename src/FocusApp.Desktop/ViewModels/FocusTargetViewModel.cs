using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusApp.Desktop.Services;

namespace FocusApp.Desktop.ViewModels;

public sealed class FocusTargetViewModel : INotifyPropertyChanged
{
    private int _totalFocusSeconds;
    private bool _isSelected;

    public FocusTargetViewModel(
        string name,
        IEnumerable<string>? taskNames = null,
        string? targetId = null,
        bool isArchived = false,
        string? iconFileName = null,
        string? iconColorHex = null)
    {
        TargetId = string.IsNullOrWhiteSpace(targetId) ? Guid.NewGuid().ToString("N") : targetId;
        Name = name;
        IconFileName = TargetIconCatalog.ResolveIconFileName(iconFileName);
        IconColorHex = iconColorHex ?? TargetIconCatalog.DefaultColorHex;
        IsArchived = isArchived;
        Tasks = new TargetTaskCollection(TargetId);
        foreach (var taskName in taskNames ?? [])
        {
            AddTask(taskName);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; private set; }

    public string TargetId { get; }

    public string IconFileName { get; private set; }

    public string IconColorHex { get; private set; }
    public string IconSource => TargetIconCatalog.GetIconSource(IconFileName, IconColorHex);

    public bool IsArchived { get; private set; }

    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<FocusTaskViewModel> Tasks { get; }

    public int TotalFocusSeconds
    {
        get => _totalFocusSeconds;
        private set
        {
            if (_totalFocusSeconds == value)
            {
                return;
            }

            _totalFocusSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalFocusDuration));
        }
    }

    public TimeSpan TotalFocusDuration => TimeSpan.FromSeconds(TotalFocusSeconds);

    public int AccumulatedFocusSeconds => TotalFocusSeconds;

    public TimeSpan AccumulatedFocusDuration => TotalFocusDuration;

    public void AddFocusDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        TotalFocusSeconds = checked(TotalFocusSeconds + (int)Math.Floor(duration.TotalSeconds));
    }

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

    public FocusTaskViewModel AddTask(
        string taskId,
        string name,
        bool isCompleted,
        bool insertAtTop = false,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? completedAtUtc = null)
    {
        var task = new FocusTaskViewModel(TargetId, name, false, taskId, createdAtUtc);
        task.ApplyCompletion(isCompleted, completedAtUtc);
        if (insertAtTop) Tasks.Insert(0, task); else Tasks.Add(task);
        return task;
    }

    public void ApplyArchived(bool archived) => IsArchived = archived;

    internal void ApplySelection(bool selected) => IsSelected = selected;

    public void ApplyName(string name)
    {
        if (Name == name) return;
        Name = name;
        OnPropertyChanged(nameof(Name));
    }

    public void ApplyIcon(string? iconFileName, string? iconColorHex = null)
    {
        var resolved = TargetIconCatalog.ResolveIconFileName(iconFileName);
        var color = iconColorHex ?? IconColorHex;
        if (string.Equals(IconFileName, resolved, StringComparison.OrdinalIgnoreCase) && IconColorHex == color) return;
        IconFileName = resolved;
        IconColorHex = color;
        OnPropertyChanged(nameof(IconFileName));
        OnPropertyChanged(nameof(IconColorHex));
        OnPropertyChanged(nameof(IconSource));
    }

    public bool RemoveTask(FocusTaskViewModel task) =>
        task.TargetId == TargetId && Tasks.Remove(task);

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
    private DateTimeOffset _createdAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private bool _isNew;
    private bool _isDragging;
    private bool _showDropBefore;
    private bool _showDropAfter;
    private bool _isCompleting;
    private bool _isCompletionStyled;
    private bool _isCompletionExiting;
    private bool _isUncompleting;
    private bool _isUncompletionRestored;
    private bool _isUncompletionExiting;
    private bool _isBatchSelected;
    private int _listPriorityRank;

    internal FocusTaskViewModel(
        string targetId,
        string name,
        bool isNew = false,
        string? taskId = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("任务必须绑定目标。", nameof(targetId));
        }

        TargetId = targetId;
        TaskId = string.IsNullOrWhiteSpace(taskId) ? Guid.NewGuid().ToString("N") : taskId;
        _name = name;
        _editName = name;
        _isNew = isNew;
        _createdAtUtc = (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TargetId { get; }

    public string TaskId { get; }

    public DateTimeOffset CreatedAtUtc => _createdAtUtc;

    public string CreatedDateDisplay
    {
        get
        {
            var localCreatedAt = CreatedAtUtc.ToLocalTime();
            return localCreatedAt.Year == DateTime.Now.Year
                ? $"{localCreatedAt.Month}月{localCreatedAt.Day}日"
                : $"{localCreatedAt.Year}年{localCreatedAt.Month}月{localCreatedAt.Day}日";
        }
    }

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
        set => ApplyCompletion(
            value,
            value ? _completedAtUtc ?? DateTimeOffset.UtcNow : null);
    }

    public DateTimeOffset? CompletedAtUtc => _completedAtUtc;

    public string CompletedTimeDisplay => CompletedAtUtc?.ToLocalTime().ToString("HH:mm") ?? "—";

    internal void ApplyCreatedAt(DateTimeOffset createdAtUtc)
    {
        var normalizedCreatedAtUtc = createdAtUtc.ToUniversalTime();
        if (_createdAtUtc == normalizedCreatedAtUtc)
        {
            return;
        }

        _createdAtUtc = normalizedCreatedAtUtc;
        OnPropertyChanged(nameof(CreatedAtUtc));
        OnPropertyChanged(nameof(CreatedDateDisplay));
    }

    public void ApplyCompletion(bool isCompleted, DateTimeOffset? completedAtUtc)
    {
        var normalizedCompletedAtUtc = isCompleted
            ? completedAtUtc?.ToUniversalTime()
            : null;
        var completionChanged = _isCompleted != isCompleted;
        var completedAtChanged = _completedAtUtc != normalizedCompletedAtUtc;
        if (!completionChanged && !completedAtChanged)
        {
            return;
        }

        _isCompleted = isCompleted;
        _completedAtUtc = normalizedCompletedAtUtc;
        if (completedAtChanged)
        {
            OnPropertyChanged(nameof(CompletedAtUtc));
            OnPropertyChanged(nameof(CompletedTimeDisplay));
        }
        if (completionChanged) OnPropertyChanged(nameof(IsCompleted));
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

    public bool IsCompleting
    {
        get => _isCompleting;
        internal set
        {
            if (_isCompleting == value)
            {
                return;
            }

            _isCompleting = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompletionStyled
    {
        get => _isCompletionStyled;
        internal set
        {
            if (_isCompletionStyled == value)
            {
                return;
            }

            _isCompletionStyled = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompletionExiting
    {
        get => _isCompletionExiting;
        internal set
        {
            if (_isCompletionExiting == value)
            {
                return;
            }

            _isCompletionExiting = value;
            OnPropertyChanged();
        }
    }

    public bool IsUncompleting
    {
        get => _isUncompleting;
        internal set
        {
            if (_isUncompleting == value)
            {
                return;
            }

            _isUncompleting = value;
            OnPropertyChanged();
        }
    }

    public bool IsUncompletionRestored
    {
        get => _isUncompletionRestored;
        internal set
        {
            if (_isUncompletionRestored == value)
            {
                return;
            }

            _isUncompletionRestored = value;
            OnPropertyChanged();
        }
    }

    public bool IsBatchSelected
    {
        get => _isBatchSelected;
        internal set
        {
            if (_isBatchSelected == value) return;
            _isBatchSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsUncompletionExiting
    {
        get => _isUncompletionExiting;
        internal set
        {
            if (_isUncompletionExiting == value)
            {
                return;
            }

            _isUncompletionExiting = value;
            OnPropertyChanged();
        }
    }

    public int ListPriorityRank
    {
        get => _listPriorityRank;
        internal set
        {
            if (_listPriorityRank == value)
            {
                return;
            }

            _listPriorityRank = value;
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

    internal void ApplyName(string name)
    {
        if (Name == name) return;
        Name = name;
        EditName = name;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
