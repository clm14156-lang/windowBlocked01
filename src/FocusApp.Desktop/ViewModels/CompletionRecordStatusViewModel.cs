using System.ComponentModel;

namespace FocusApp.Desktop.ViewModels;

public enum CompletionRecordSaveState { Saving, LocalSaved, Uploading, Synced }

// Only disk/database confirmations and the upload worker update this record-scoped state.
public sealed class CompletionRecordStatusViewModel : INotifyPropertyChanged
{
    private sealed record Entry(CompletionRecordSaveState State, Guid? OperationId = null);
    private readonly object sync = new();
    private readonly Dictionary<Guid, Entry> records = new();
    private Guid? currentRecordId;

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid? CurrentRecordId { get { lock (sync) return currentRecordId; } }
    public CompletionRecordSaveState State
    {
        get { lock (sync) return currentRecordId is { } id && records.TryGetValue(id, out var entry)
            ? entry.State : CompletionRecordSaveState.Saving; }
    }
    public bool IsVisible => CurrentRecordId is not null && State != CompletionRecordSaveState.Saving;

    public void Track(Guid recordId, bool isSaved = false)
    {
        lock (sync)
        {
            currentRecordId = recordId;
            if (!records.TryGetValue(recordId, out var entry) || isSaved && entry.State == CompletionRecordSaveState.Saving)
                records[recordId] = new(isSaved ? CompletionRecordSaveState.LocalSaved : CompletionRecordSaveState.Saving);
            Trim();
        }
        Notify();
    }

    internal void Clear()
    {
        lock (sync)
        {
            records.Clear();
            currentRecordId = null;
        }
        Notify();
    }

    public void LocalSaved(Guid recordId)
    {
        bool notify;
        lock (sync)
        {
            if (records.TryGetValue(recordId, out var entry) && entry.State != CompletionRecordSaveState.Saving) return;
            records[recordId] = new(CompletionRecordSaveState.LocalSaved);
            notify = currentRecordId == recordId;
            Trim();
        }
        if (notify) Notify();
    }

    public void UploadStarted(Guid recordId, Guid operationId)
    {
        bool notify;
        lock (sync)
        {
            if (!records.TryGetValue(recordId, out var entry) || entry.State == CompletionRecordSaveState.Saving) return;
            records[recordId] = new(CompletionRecordSaveState.Uploading, operationId);
            notify = currentRecordId == recordId;
        }
        if (notify) Notify();
    }

    public void UploadFinished(Guid recordId, Guid operationId, bool confirmed)
    {
        bool notify;
        lock (sync)
        {
            if (!records.TryGetValue(recordId, out var entry) || entry.OperationId != operationId) return;
            records[recordId] = new(confirmed ? CompletionRecordSaveState.Synced : CompletionRecordSaveState.LocalSaved);
            notify = currentRecordId == recordId;
        }
        if (notify) Notify();
    }

    public void CancelUploads()
    {
        var notify = false;
        lock (sync)
        {
            foreach (var pair in records.Where(x => x.Value.State == CompletionRecordSaveState.Uploading).ToArray())
            {
                records[pair.Key] = new(CompletionRecordSaveState.LocalSaved);
                notify |= currentRecordId == pair.Key;
            }
        }
        if (notify) Notify();
    }

    private void Trim()
    {
        while (records.Count > 256)
            records.Remove(records.Keys.First(id => id != currentRecordId));
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new(nameof(CurrentRecordId)));
        PropertyChanged?.Invoke(this, new(nameof(State)));
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
    }
}
