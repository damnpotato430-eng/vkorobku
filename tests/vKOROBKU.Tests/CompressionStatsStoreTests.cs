using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class CompressionStatsStoreTests
{
    [Fact]
    public void RecordCompression_AccumulatesTotalsAndPerDrive()
    {
        var path = CreateTempPath();
        try
        {
            var store = new CompressionStatsStore(path);
            store.RecordCompression(@"F:\", 100);
            store.RecordCompression(@"f:\", 50);
            var stats = store.RecordCompression(@"G:\", 200);

            Assert.Equal(350, stats.FreedBytes);
            Assert.Equal(3, stats.Operations);
            Assert.Equal(150, stats.Drives[@"F:\"].FreedBytes);
            Assert.Equal(2, stats.Drives[@"F:\"].Operations);
            Assert.Equal(200, stats.Drives[@"G:\"].FreedBytes);
            Assert.NotNull(stats.FirstOperationAt);
            Assert.NotNull(stats.LastOperationAt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_AfterRecord_RoundTripsThroughFile()
    {
        var path = CreateTempPath();
        try
        {
            var first = new CompressionStatsStore(path);
            first.RecordCompression(@"E:\", 1234);

            var reloaded = new CompressionStatsStore(path).Load();

            Assert.Equal(1234, reloaded.FreedBytes);
            Assert.Equal(1, reloaded.Operations);
            Assert.Equal(1234, reloaded.Drives[@"E:\"].FreedBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecordCompression_NegativeFreedBytes_CountsOperationOnly()
    {
        var path = CreateTempPath();
        try
        {
            var stats = new CompressionStatsStore(path).RecordCompression(@"C:\", -5);

            Assert.Equal(0, stats.FreedBytes);
            Assert.Equal(1, stats.Operations);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsEmpty()
    {
        var path = CreateTempPath();
        try
        {
            File.WriteAllText(path, "{ not json");
            var stats = new CompressionStatsStore(path).Load();

            Assert.Equal(0, stats.FreedBytes);
            Assert.Equal(0, stats.Operations);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"vkorobku-stats-{Guid.NewGuid():N}.json");
}
