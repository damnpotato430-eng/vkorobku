using vKOROBKU.App.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;
using vKOROBKU.App.Models;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.Services;

public sealed class OperationJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "operations.json");
    private readonly object _sync = new();

    public Guid Begin(WorkerJob job) =>
        Begin(job.RootPath, job.Operation, job.Algorithm, Strings.Journal_WaitingWorker);

    public Guid Begin(string installPath, string operation, string? algorithm, string message)
    {
        var id = Guid.NewGuid();
        lock (_sync)
        {
            var entries = Read();
            entries.Add(new OperationJournalEntry(
                id, installPath, operation, algorithm, DateTimeOffset.Now, null,
                OperationJournalState.Running, 0, 0, 0, 0, 0, message));
            Write(entries);
        }
        return id;
    }

    public IReadOnlyList<OperationJournalEntry> Load()
    {
        lock (_sync)
            return Read().OrderByDescending(entry => entry.StartedAt).ToArray();
    }

    public void Update(Guid id, WorkerMessage message)
    {
        lock (_sync)
        {
            var entries = Read();
            var index = entries.FindIndex(entry => entry.Id == id);
            if (index < 0)
                return;
            var current = entries[index];
            entries[index] = current with
            {
                ProcessedBytes = message.ProcessedBytes,
                TotalBytes = message.TotalBytes,
                ProcessedFiles = message.ProcessedFiles,
                TotalFiles = message.TotalFiles,
                ErrorCount = message.ErrorCount,
                Message = message.Text
            };
            Write(entries);
        }
    }

    public void Finish(Guid id, OperationJournalState state, string? message)
    {
        lock (_sync)
        {
            var entries = Read();
            var index = entries.FindIndex(entry => entry.Id == id);
            if (index < 0)
                return;
            entries[index] = entries[index] with
            {
                FinishedAt = DateTimeOffset.Now,
                State = state,
                Message = message
            };
            Write(entries);
        }
    }

    public int MarkInterrupted()
    {
        lock (_sync)
        {
            var entries = Read();
            var count = 0;
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].State != OperationJournalState.Running)
                    continue;
                entries[index] = entries[index] with
                {
                    FinishedAt = DateTimeOffset.Now,
                    State = OperationJournalState.Interrupted,
                    Message = Strings.Journal_InterruptedByExit
                };
                count++;
            }
            if (count > 0)
                Write(entries);
            return count;
        }
    }

    // A running operation survives the wipe: its entry is still being updated by
    // the worker pump and feeds the "current operation" card.
    public void Clear()
    {
        lock (_sync)
        {
            var entries = Read();
            var kept = entries.Where(entry => entry.State == OperationJournalState.Running).ToList();
            if (kept.Count != entries.Count)
                Write(kept);
        }
    }

    private List<OperationJournalEntry> Read()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize<List<OperationJournalEntry>>(File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }

    private void Write(List<OperationJournalEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries.TakeLast(200), JsonOptions));
        File.Move(temporary, _path, true);
    }
}
