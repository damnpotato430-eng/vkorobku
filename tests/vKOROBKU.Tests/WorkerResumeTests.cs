using vKOROBKU.Worker;

namespace vKOROBKU.Tests;

public sealed class WorkerResumeTests
{
    [Fact]
    public void PartitionPendingFiles_SkipsProcessedFilesAndCountsThem()
    {
        var files = new[]
        {
            new WorkerFile(@"C:\Game\a.pak", 10),
            new WorkerFile(@"C:\Game\b.pak", 20),
            new WorkerFile(@"C:\Game\c.pak", 30)
        };

        var (pending, skippedBytes, skippedFiles) = Program.PartitionPendingFiles(
            files, file => file.Path.EndsWith("b.pak", StringComparison.Ordinal), CancellationToken.None);

        Assert.Equal(new[] { @"C:\Game\a.pak", @"C:\Game\c.pak" }, pending.Select(file => file.Path));
        Assert.Equal(20, skippedBytes);
        Assert.Equal(1, skippedFiles);
    }

    [Fact]
    public void PartitionPendingFiles_NothingProcessed_KeepsAllFiles()
    {
        var files = new[] { new WorkerFile(@"C:\Game\a.pak", 10) };

        var (pending, skippedBytes, skippedFiles) = Program.PartitionPendingFiles(
            files, _ => false, CancellationToken.None);

        Assert.Single(pending);
        Assert.Equal(0, skippedBytes);
        Assert.Equal(0, skippedFiles);
    }

    [Fact]
    public void PartitionPendingFiles_EverythingProcessed_LeavesNoPendingWork()
    {
        var files = new[]
        {
            new WorkerFile(@"C:\Game\a.pak", 10),
            new WorkerFile(@"C:\Game\b.pak", 20)
        };

        var (pending, skippedBytes, skippedFiles) = Program.PartitionPendingFiles(
            files, _ => true, CancellationToken.None);

        Assert.Empty(pending);
        Assert.Equal(30, skippedBytes);
        Assert.Equal(2, skippedFiles);
    }
}
