using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FocusApp.Desktop.Services;
using FocusApp.Contracts;
using FocusApp.Core;

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
    private string _description = string.Empty;
    private bool _isExpanded;
    private bool _isDrawerSelected;
    private int _drawerNumber;
    private bool _applyingDetails;
    private string _name;
    private string _editName;
    private bool _isFocusMenuOpen;
    private bool _isEditing;
    private bool _isCompleted;
    private DateTimeOffset _createdAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private bool _isNew;

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
        SubTasks.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (FocusSubTaskViewModel item in e.OldItems) item.PropertyChanged -= SubTaskChanged;
            if (e.NewItems is not null)
                foreach (FocusSubTaskViewModel item in e.NewItems) item.PropertyChanged += SubTaskChanged;
            NotifyDetailsChanged();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DetailsChanged;

    internal bool IsUpdatingDetails { get; private set; }
    internal bool CompletionChangedWithDetails { get; private set; }

    public ObservableCollection<FocusSubTaskViewModel> SubTasks { get; } = [];

    // Keep the stored order intact so unchecking an item restores its original position.
    public IEnumerable<FocusSubTaskViewModel> SortedSubTasks => SubTasks.OrderBy(item => item.IsCompleted);
    public string Description
    {
        get => _description;
        set
        {
            if (_description == value) return;
            _description = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDescription));
            OnPropertyChanged(nameof(RemarkActionLabel));
            if (!_applyingDetails) DetailsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string RemarkActionLabel => HasDescription ? "编辑备注" : "添加备注";
    public bool HasSubTasks => SubTasks.Count > 0;
    public bool HasExpandedSubTasks => HasSubTasks && IsExpanded;
    public string SubTaskProgress => $"子任务 · {SubTasks.Count(item => item.IsCompleted)}/{SubTasks.Count}";
    public bool CanAddSubTask => SubTasks.Count < 20;
    public string DrawerSubTaskToggleLabel => IsExpanded ? "收起" : $"子任务 {SubTasks.Count(item => item.IsCompleted)}/{SubTasks.Count}";
    public bool IsDrawerSelected
    {
        get => _isDrawerSelected;
        internal set { if (_isDrawerSelected == value) return; _isDrawerSelected = value; OnPropertyChanged(); }
    }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DrawerSubTaskToggleLabel));
            OnPropertyChanged(nameof(HasExpandedSubTasks));
        }
    }
    public int DrawerNumber
    {
        get => _drawerNumber;
        internal set { if (_drawerNumber == value) return; _drawerNumber = value; OnPropertyChanged(); }
    }

    private void SubTaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusSubTaskViewModel.Title) or nameof(FocusSubTaskViewModel.IsCompleted))
            NotifyDetailsChanged();
    }

    private void NotifyDetailsChanged()
    {
        // Publish child edits and any resulting parent completion as one details update.
        IsUpdatingDetails = true;
        CompletionChangedWithDetails = false;
        try
        {
            if (!_applyingDetails)
                ApplyCompletion(_isCompleted, _completedAtUtc);
            if (!HasSubTasks) IsExpanded = false;
            OnPropertyChanged(nameof(SortedSubTasks));
            OnPropertyChanged(nameof(SubTaskProgress));
            OnPropertyChanged(nameof(CanAddSubTask));
            OnPropertyChanged(nameof(DrawerSubTaskToggleLabel));
            OnPropertyChanged(nameof(HasSubTasks));
            OnPropertyChanged(nameof(HasExpandedSubTasks));
            if (!_applyingDetails) DetailsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsUpdatingDetails = false;
            CompletionChangedWithDetails = false;
        }
    }

    internal void CompleteSubTasks()
    {
        // Batch the child checks so the session publishes one completion and one save.
        var wasApplying = _applyingDetails;
        _applyingDetails = true;
        try
        {
            foreach (var child in SubTasks) child.IsCompleted = true;
        }
        finally { _applyingDetails = wasApplying; }
        NotifyDetailsChanged();
    }

    internal void ApplyDetails(LocalTaskDto source)
    {
        _applyingDetails = true;
        try
        {
            Description = source.Description;
            var desired = source.SubTasks.OrderBy(item => item.SortOrder).ToArray();
            foreach (var removed in SubTasks.Where(item => desired.All(value => value.Id != item.Id)).ToArray())
                SubTasks.Remove(removed);
            for (var index = 0; index < desired.Length; index++)
            {
                var subSource = desired[index];
                var item = SubTasks.FirstOrDefault(value => value.Id == subSource.Id);
                if (item is null)
                {
                    item = new FocusSubTaskViewModel(subSource);
                    SubTasks.Insert(index, item);
                }
                else
                {
                    item.Apply(subSource);
                    var oldIndex = SubTasks.IndexOf(item);
                    if (oldIndex != index) SubTasks.Move(oldIndex, index);
                }
            }
            // Judge the incoming parent state only after all child updates are applied.
            ApplyCompletion(source.IsCompleted, source.CompletedAtUtc);
        }
        finally { _applyingDetails = false; }
    }

    public IReadOnlyList<LocalSubTaskDto> ExportSubTasks() => SubTasks.Select((item, index) =>
        new LocalSubTaskDto(item.Id, TaskId, item.Title, item.IsCompleted, index, item.CreatedAtUtc, item.UpdatedAtUtc)).ToArray();

    public LocalTaskDetailsSnapshotDto CaptureHistoryDetails() => new(Description,
        SubTasks.Select(child => new LocalSubTaskSnapshotDto(child.Title, child.IsCompleted)).ToArray());

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
        var requestedCompletion = isCompleted;
        isCompleted = TaskCompletionPolicy.IsCompleted(isCompleted, SubTasks.Select(child => child.IsCompleted));
        var normalizedCompletedAtUtc = isCompleted
            ? completedAtUtc?.ToUniversalTime()
            : null;
        var completionChanged = _isCompleted != isCompleted;
        var completedAtChanged = _completedAtUtc != normalizedCompletedAtUtc;
        if (!completionChanged && !completedAtChanged)
        {
            // A checkbox toggles before its command runs; reapply the rejected state to the binding.
            if (requestedCompletion != isCompleted) OnPropertyChanged(nameof(IsCompleted));
            return;
        }

        _isCompleted = isCompleted;
        _completedAtUtc = normalizedCompletedAtUtc;
        if (IsUpdatingDetails && completionChanged) CompletionChangedWithDetails = true;
        if (completedAtChanged)
        {
            OnPropertyChanged(nameof(CompletedAtUtc));
            OnPropertyChanged(nameof(CompletedTimeDisplay));
        }
        if (completionChanged || requestedCompletion != isCompleted) OnPropertyChanged(nameof(IsCompleted));
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
