namespace vKOROBKU.Worker;

internal static class BatchPlanner
{
    internal const int MaximumFilesPerBatch = 200;
    internal const int MaximumCommandLength = 24_000;
    internal const long MaximumBatchBytes = 256L * 1024 * 1024;
    private const int BaseCommandLength = 100;

    internal static IReadOnlyList<List<WorkerFile>> CreateBatches(IReadOnlyList<WorkerFile> files)
    {
        var batches = new List<List<WorkerFile>>();
        var current = new List<WorkerFile>();
        var commandLength = BaseCommandLength;
        long batchBytes = 0;

        foreach (var file in files)
        {
            var argumentLength = file.Path.Length + 3;
            if (current.Count > 0 &&
                (current.Count >= MaximumFilesPerBatch ||
                 commandLength + argumentLength > MaximumCommandLength ||
                 batchBytes + file.Length > MaximumBatchBytes))
            {
                batches.Add(current);
                current = new List<WorkerFile>();
                commandLength = BaseCommandLength;
                batchBytes = 0;
            }

            current.Add(file);
            commandLength += argumentLength;
            batchBytes += file.Length;
        }

        if (current.Count > 0)
            batches.Add(current);
        return batches;
    }
}

internal sealed record WorkerFile(string Path, long Length);
