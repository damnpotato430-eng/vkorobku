using vKOROBKU.Worker;

namespace vKOROBKU.Tests;

public sealed class BatchPlannerTests
{
    private const long MiB = 1024L * 1024;

    [Fact]
    public void CreateBatches_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(BatchPlanner.CreateBatches([]));
    }

    [Fact]
    public void CreateBatches_RespectsTwoHundredFileLimit()
    {
        var files = Enumerable.Range(0, 201)
            .Select(index => new WorkerFile($"C:\\Game\\file-{index}.bin", 1))
            .ToArray();

        var batches = BatchPlanner.CreateBatches(files);

        Assert.Equal(2, batches.Count);
        Assert.Equal(200, batches[0].Count);
        Assert.Single(batches[1]);
    }

    [Fact]
    public void CreateBatches_RespectsCommandLineLengthLimit()
    {
        var longName = new string('a', 11_980);
        var files = new[]
        {
            new WorkerFile($"C:\\{longName}-one.bin", 1),
            new WorkerFile($"C:\\{longName}-two.bin", 1)
        };

        var batches = BatchPlanner.CreateBatches(files);

        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch =>
            Assert.True(100 + batch.Sum(file => file.Path.Length + 3) <= BatchPlanner.MaximumCommandLength));
    }

    [Fact]
    public void CreateBatches_RespectsByteLimit()
    {
        var files = new[]
        {
            new WorkerFile("D:\\Game\\first.pak", 200 * MiB),
            new WorkerFile("D:\\Game\\second.pak", 100 * MiB),
            new WorkerFile("D:\\Game\\third.pak", 100 * MiB)
        };

        var batches = BatchPlanner.CreateBatches(files);

        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch =>
            Assert.True(batch.Sum(file => file.Length) <= BatchPlanner.MaximumBatchBytes));
    }
}
