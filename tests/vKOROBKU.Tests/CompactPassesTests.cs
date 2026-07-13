extern alias worker;

using vKOROBKU.Worker;
using WorkerJob = worker::vKOROBKU.Protocol.WorkerJob;

namespace vKOROBKU.Tests;

public sealed class CompactPassesTests
{
    [Fact]
    public void CreateCompactPasses_Compress_UsesSingleAlgorithmPass()
    {
        var passes = Program.CreateCompactPasses(new WorkerJob(@"C:\Games\Demo", "compress", "XPRESS16K"));

        var pass = Assert.Single(passes);
        Assert.Equal(new[] { "/C", "/I", "/F", "/Q", "/EXE:XPRESS16K" }, pass);
    }

    [Fact]
    public void CreateCompactPasses_Decompress_RemovesWofBackingAndNtfsCompression()
    {
        var passes = Program.CreateCompactPasses(new WorkerJob(@"C:\Games\Demo", "decompress", null));

        Assert.Equal(2, passes.Count);
        Assert.Equal(new[] { "/U", "/EXE", "/I", "/F", "/Q" }, passes[0]);
        Assert.Equal(new[] { "/U", "/I", "/F", "/Q" }, passes[1]);
    }
}
