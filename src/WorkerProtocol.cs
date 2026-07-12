namespace vKOROBKU.Protocol;

public sealed record WorkerJob(
    string RootPath,
    string Operation,
    string? Algorithm);

public sealed record WorkerCommand(string Type);

public sealed record WorkerMessage(
    string Type,
    string? Text = null,
    string? Token = null,
    long ProcessedBytes = 0,
    long TotalBytes = 0,
    int ProcessedFiles = 0,
    int TotalFiles = 0,
    int ErrorCount = 0,
    long PhysicalBefore = 0,
    long PhysicalAfter = 0);
