namespace vKOROBKU.App.Models;

public enum OperationJournalState
{
    Running,
    Completed,
    Cancelled,
    Failed,
    Interrupted
}

public sealed record OperationJournalEntry(
    Guid Id,
    string InstallPath,
    string Operation,
    string? Algorithm,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    OperationJournalState State,
    long ProcessedBytes,
    long TotalBytes,
    int ProcessedFiles,
    int TotalFiles,
    int ErrorCount,
    string? Message);
