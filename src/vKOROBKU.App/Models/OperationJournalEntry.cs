using System.Text.Json.Serialization;
using vKOROBKU.App.Resources;

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
    string? Message)
{
    [JsonIgnore]
    public string GameName => Path.GetFileName(InstallPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    [JsonIgnore]
    public string OperationText => Operation switch
    {
        "analysis" => Strings.Journal_OperationAnalysis,
        "compress" => $"{Strings.Journal_OperationCompress}{(string.IsNullOrWhiteSpace(Algorithm) ? string.Empty : $" · {Algorithm}")}",
        "decompress" => Strings.Journal_OperationDecompress,
        _ => Operation
    };

    [JsonIgnore]
    public string StateText => State switch
    {
        OperationJournalState.Running => Strings.Journal_StateRunning,
        OperationJournalState.Completed => Strings.Journal_StateCompleted,
        OperationJournalState.Cancelled => Strings.Journal_StateCancelled,
        OperationJournalState.Failed => Strings.Journal_StateFailed,
        OperationJournalState.Interrupted => Strings.Journal_StateInterrupted,
        _ => State.ToString()
    };

    [JsonIgnore]
    public double ProgressPercent => Operation == "analysis" && TotalFiles > 0
        ? Math.Clamp(ProcessedFiles * 100d / TotalFiles, 0, 100)
        : TotalBytes > 0
            ? Math.Clamp(ProcessedBytes * 100d / TotalBytes, 0, 100)
            : TotalFiles > 0
                ? Math.Clamp(ProcessedFiles * 100d / TotalFiles, 0, 100)
                : State == OperationJournalState.Completed ? 100 : 0;

    [JsonIgnore]
    public string ProgressText => Operation == "analysis"
        ? TotalBytes > 0
            ? string.Format(Strings.Journal_ProgressStageBytes,
                $"{ProgressPercent:0}", ByteFormatter.Format(ProcessedBytes), ByteFormatter.Format(TotalBytes))
            : $"{ProgressPercent:0}%"
        : TotalBytes > 0
            ? string.Format(Strings.Journal_ProgressBytes,
                $"{ProgressPercent:0}", ByteFormatter.Format(ProcessedBytes), ByteFormatter.Format(TotalBytes))
            : TotalFiles > 0
                ? string.Format(Strings.Journal_ProgressFiles,
                    $"{ProgressPercent:0}", $"{ProcessedFiles:N0}", $"{TotalFiles:N0}")
                : $"{ProgressPercent:0}%";

    [JsonIgnore]
    public string StartedText => StartedAt.ToString("dd.MM.yyyy HH:mm");
}
