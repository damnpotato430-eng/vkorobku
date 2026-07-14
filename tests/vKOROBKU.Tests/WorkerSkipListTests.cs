using vKOROBKU.Worker;

namespace vKOROBKU.Tests;

public sealed class WorkerSkipListTests
{
    private static readonly HashSet<string> Extensions = new(
        [".bik", ".mp4"], StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void IsSkipListed_ListedExtension_IsCaseInsensitive()
    {
        Assert.True(Program.IsSkipListed(new WorkerFile(@"C:\Game\intro.BIK", 500_000_000), Extensions, 4096));
        Assert.True(Program.IsSkipListed(new WorkerFile(@"C:\Game\video.Mp4", 500_000_000), Extensions, 4096));
    }

    [Fact]
    public void IsSkipListed_FileNoLargerThanCluster_IsSkipped()
    {
        Assert.True(Program.IsSkipListed(new WorkerFile(@"C:\Game\tiny.txt", 4096), Extensions, 4096));
    }

    [Fact]
    public void IsSkipListed_RegularFile_IsNotSkipped()
    {
        Assert.False(Program.IsSkipListed(new WorkerFile(@"C:\Game\data.pak", 500_000_000), Extensions, 4096));
    }
}
