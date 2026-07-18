using vKOROBKU.App.Models;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.Services;

/// <summary>Carries one operation-journal entry through its lifecycle: begin, progress
/// updates, and the terminal transition. The published entry drives the operations list
/// in the UI; store failures never propagate — the journal is diagnostics, not state
/// the operation depends on.</summary>
public sealed class OperationTracker
{
    private readonly OperationJournalStore _store;
    private readonly Action<OperationJournalEntry> _publish;
    private OperationJournalEntry _entry;

    private OperationTracker(
        OperationJournalStore store, Action<OperationJournalEntry> publish, OperationJournalEntry entry)
    {
        _store = store;
        _publish = publish;
        _entry = entry;
        publish(entry);
    }

    public Guid Id => _entry.Id;

    public static OperationTracker Begin(
        OperationJournalStore store, Action<OperationJournalEntry> publish, WorkerJob job, string message)
    {
        Guid id;
        try { id = store.Begin(job); }
        catch { id = Guid.NewGuid(); }
        return new OperationTracker(store, publish, new OperationJournalEntry(
            id, job.RootPath, job.Operation, job.Algorithm, DateTimeOffset.Now, null,
            OperationJournalState.Running, 0, 0, 0, 0, 0, message));
    }

    public static OperationTracker BeginAnalysis(
        OperationJournalStore store, Action<OperationJournalEntry> publish, string installPath, string message)
    {
        Guid id;
        try { id = store.Begin(installPath, "analysis", null, message); }
        catch { id = Guid.NewGuid(); }
        return new OperationTracker(store, publish, new OperationJournalEntry(
            id, installPath, "analysis", null, DateTimeOffset.Now, null,
            OperationJournalState.Running, 0, 0, 0, 0, 0, message));
    }

    /// <summary>Publishes the counters to the UI entry; persists to the journal file
    /// unless the caller throttles (analysis reports every percent but stores every
    /// fifth one).</summary>
    public void ReportProgress(WorkerMessage message, string displayMessage, bool persist = true)
    {
        _entry = _entry with
        {
            ProcessedBytes = message.ProcessedBytes,
            TotalBytes = message.TotalBytes,
            ProcessedFiles = message.ProcessedFiles,
            TotalFiles = message.TotalFiles,
            ErrorCount = message.ErrorCount,
            Message = displayMessage
        };
        _publish(_entry);
        if (!persist)
            return;
        try { _store.Update(Id, message); } catch { }
    }

    /// <summary>Terminal transition. Result counters, when provided, reach both the UI
    /// entry and the persisted record.</summary>
    public void Finish(OperationJournalState state, string message, WorkerMessage? result = null)
    {
        _entry = _entry with
        {
            FinishedAt = DateTimeOffset.Now,
            State = state,
            Message = message
        };
        if (result is not null)
        {
            _entry = _entry with
            {
                ProcessedBytes = result.ProcessedBytes,
                TotalBytes = result.TotalBytes,
                ProcessedFiles = result.ProcessedFiles,
                TotalFiles = result.TotalFiles,
                ErrorCount = result.ErrorCount
            };
        }
        _publish(_entry);
        try
        {
            if (result is not null)
                _store.Update(Id, result);
            _store.Finish(Id, state, message);
        }
        catch { }
    }
}
