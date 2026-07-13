using System.Text.Json.Serialization;

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
        "analysis" => "Анализ",
        "compress" => $"Сжатие{(string.IsNullOrWhiteSpace(Algorithm) ? string.Empty : $" · {Algorithm}")}",
        "decompress" => "Распаковка",
        _ => Operation
    };

    [JsonIgnore]
    public string StateText => State switch
    {
        OperationJournalState.Running => "Выполняется",
        OperationJournalState.Completed => "Завершено",
        OperationJournalState.Cancelled => "Отменено",
        OperationJournalState.Failed => "Ошибка",
        OperationJournalState.Interrupted => "Прервано",
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
            ? $"{ProgressPercent:0}% · {ByteFormatter.Format(ProcessedBytes)} из {ByteFormatter.Format(TotalBytes)} на текущем этапе"
            : $"{ProgressPercent:0}%"
        : TotalBytes > 0
            ? $"{ProgressPercent:0}% · {ByteFormatter.Format(ProcessedBytes)} из {ByteFormatter.Format(TotalBytes)}"
            : TotalFiles > 0
                ? $"{ProgressPercent:0}% · {ProcessedFiles:N0} из {TotalFiles:N0} файлов"
                : $"{ProgressPercent:0}%";

    [JsonIgnore]
    public string StartedText => StartedAt.ToString("dd.MM.yyyy HH:mm");
}
